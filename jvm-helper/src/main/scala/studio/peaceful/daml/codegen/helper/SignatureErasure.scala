// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import com.digitalasset.daml.lf.archive.Dar
import com.digitalasset.daml.lf.data.Ref.PackageId
import com.digitalasset.daml.lf.language.Ast

/** Erases expression bodies from a fully-decoded `Ast.Package`, producing
  * an `Ast.PackageSignature` (a `GenPackage[Unit]`).
  *
  * Also strips non-serializable `DDataType` definitions. The stable
  * `daml-lf-archive` 3.4.11 API removed the `onlySerializableDataDefs`
  * parameter, so `decodeArchivePayload` now returns all definitions including
  * non-serializable ones. `decodeArchivePayloadSchema` filters them
  * internally. This filter ensures the two decode paths produce byte-equivalent
  * `IntermediateDar` proto output — the weaker guarantee that `DecodeSpec`
  * actually verifies. The in-memory `PackageSignature` objects may still
  * differ (e.g. in `DValue` entries) because `AstToIntermediate.translate`
  * ignores `DValueSignature` and never emits them into the proto.
  *
  * This co-locates with [[AstToIntermediate]] all coupling to
  * `daml-lf-archive` Scala case-class shapes: if DA rebases its AST,
  * the changes land here and in `AstToIntermediate`, and nowhere else.
  */
object SignatureErasure {

  def erasePackage(pkg: Ast.Package): Ast.PackageSignature =
    Ast.PackageSignature(
      modules = pkg.modules.map { case (modName, mod) => modName -> eraseModule(mod) },
      directDeps = pkg.directDeps,
      languageVersion = pkg.languageVersion,
      metadata = pkg.metadata,
      imports = pkg.imports,
    )

  def eraseDar(
      dar: Dar[(PackageId, Ast.Package)]
  ): Dar[(PackageId, Ast.PackageSignature)] =
    Dar(
      main = dar.main._1 -> erasePackage(dar.main._2),
      dependencies = dar.dependencies.map { case (id, pkg) => id -> erasePackage(pkg) },
    )

  private def eraseModule(mod: Ast.Module): Ast.ModuleSignature =
    Ast.ModuleSignature(
      name = mod.name,
      definitions = mod.definitions
        .filter { case (_, dt: Ast.DDataType) => dt.serializable; case _ => true }
        .map { case (n, d) => n -> eraseDefinition(d) },
      templates = mod.templates.map { case (n, t) => n -> eraseTemplate(t) },
      exceptions = mod.exceptions.map { case (n, _) => n -> Ast.DefExceptionSignature },
      interfaces = mod.interfaces.map { case (n, i) => n -> eraseInterface(i) },
      featureFlags = mod.featureFlags,
    )

  private def eraseDefinition(d: Ast.Definition): Ast.DefinitionSignature = d match {
    case dt: Ast.DDataType         => dt
    case Ast.GenDValue(typ, _)     => Ast.DValueSignature(typ, ())
    case Ast.DTypeSyn(params, typ) => Ast.DTypeSyn(params, typ)
  }

  private def eraseTemplate(t: Ast.Template): Ast.TemplateSignature =
    Ast.TemplateSignature(
      param = t.param,
      precond = (),
      signatories = (),
      choices = t.choices.map { case (n, c) => n -> eraseChoice(c) },
      observers = (),
      key = t.key.map(k => Ast.TemplateKeySignature(typ = k.typ, body = (), maintainers = ())),
      implements = t.implements.map { case (id, impl) =>
        id -> Ast.TemplateImplementsSignature(
          interfaceId = impl.interfaceId,
          body = Ast.InterfaceInstanceBodySignature(
            methods = impl.body.methods.map { case (mn, _) =>
              mn -> Ast.InterfaceInstanceMethodSignature(mn, ())
            },
            view = (),
          ),
        )
      },
    )

  private def eraseChoice(c: Ast.TemplateChoice): Ast.TemplateChoiceSignature =
    Ast.TemplateChoiceSignature(
      name = c.name,
      consuming = c.consuming,
      controllers = (),
      choiceObservers = c.choiceObservers.map(_ => ()),
      choiceAuthorizers = c.choiceAuthorizers.map(_ => ()),
      selfBinder = c.selfBinder,
      argBinder = c.argBinder,
      returnType = c.returnType,
      update = (),
    )

  private def eraseInterface(i: Ast.DefInterface): Ast.DefInterfaceSignature =
    Ast.DefInterfaceSignature(
      requires = i.requires,
      param = i.param,
      choices = i.choices.map { case (n, c) => n -> eraseChoice(c) },
      methods = i.methods,
      view = i.view,
      coImplements = i.coImplements.map { case (tid, ci) =>
        tid -> Ast.InterfaceCoImplementsSignature(
          templateId = ci.templateId,
          body = Ast.InterfaceInstanceBodySignature(
            methods = ci.body.methods.map { case (mn, _) =>
              mn -> Ast.InterfaceInstanceMethodSignature(mn, ())
            },
            view = (),
          ),
        )
      },
    )
}
