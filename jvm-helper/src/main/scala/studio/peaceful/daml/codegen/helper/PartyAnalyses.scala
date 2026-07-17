// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import com.digitalasset.daml.lf.archive.Dar
import com.digitalasset.daml.lf.data.Ref.{ChoiceName, DottedName, PackageId}
import com.digitalasset.daml.lf.language.Ast

/** Static party verdicts for one template: its `signatories` and `observers`,
  * and the `controllers` / `choiceObservers` of each choice declared directly
  * on it. A template with no recorded analysis defaults to [[TemplatePartyAnalysis.dynamic]].
  */
final case class TemplatePartyAnalysis(
    signatories: PartyAnalysisResult,
    observers: PartyAnalysisResult,
    choices: Map[ChoiceName, ChoicePartyAnalysis],
)

object TemplatePartyAnalysis {
  val dynamic: TemplatePartyAnalysis =
    TemplatePartyAnalysis(
      signatories = PartyAnalysisResult.Dynamic,
      observers = PartyAnalysisResult.Dynamic,
      choices = Map.empty,
    )
}

final case class ChoicePartyAnalysis(
    controllers: PartyAnalysisResult,
    observers: PartyAnalysisResult,
)

object ChoicePartyAnalysis {
  val dynamic: ChoicePartyAnalysis =
    ChoicePartyAnalysis(PartyAnalysisResult.Dynamic, PartyAnalysisResult.Dynamic)
}

/** Verdicts keyed by `(packageId, moduleName, templateName)`, produced by
  * [[PartyAnalyses.compute]] and queried by [[PartyAnalyses.lookup]].
  */
final case class PartyAnalyses(
    byTemplate: Map[(PackageId, DottedName, DottedName), TemplatePartyAnalysis]
) {
  def lookup(
      packageId: PackageId,
      moduleName: DottedName,
      templateName: DottedName,
  ): TemplatePartyAnalysis =
    byTemplate.getOrElse((packageId, moduleName, templateName), TemplatePartyAnalysis.dynamic)
}

object PartyAnalyses {

  val empty: PartyAnalyses = PartyAnalyses(Map.empty)

  /** Runs [[PartyExpressionAnalyzer]] over every template, and every choice
    * declared directly on each template, in a fully-decoded DAR. Interface
    * choices and interface implementations on templates are not analysed;
    * their choices are left [[ChoicePartyAnalysis.dynamic]].
    */
  def compute(dar: Dar[(PackageId, Ast.Package)]): PartyAnalyses = {
    val builder = Map.newBuilder[(PackageId, DottedName, DottedName), TemplatePartyAnalysis]
    (dar.main +: dar.dependencies).foreach { case (packageId, pkg) =>
      val resolveValue = samePackageValueResolver(packageId, pkg)
      pkg.modules.foreach { case (modName, mod) =>
        mod.templates.foreach { case (tmplName, tmpl) =>
          builder += ((packageId, modName, tmplName) -> analyseTemplate(tmpl, packageId, resolveValue))
        }
      }
    }
    PartyAnalyses(builder.result())
  }

  /** A [[PartyExpressionAnalyzer.ValueResolver]] that resolves a top-level
    * value reference to its defining expression within `pkg` only, returning
    * None when the reference targets a different package or names a
    * module/value that `pkg` does not define.
    */
  def samePackageValueResolver(
      packageId: PackageId,
      pkg: Ast.Package,
  ): PartyExpressionAnalyzer.ValueResolver =
    ref =>
      if (ref.packageId != packageId) None
      else
        pkg.modules
          .get(ref.qualifiedName.module)
          .flatMap(_.definitions.get(ref.qualifiedName.name))
          .collect { case Ast.DValue(_, body) => body }

  private def analyseTemplate(
      tmpl: Ast.Template,
      currentPackageId: PackageId,
      resolveValue: PartyExpressionAnalyzer.ValueResolver,
  ): TemplatePartyAnalysis =
    TemplatePartyAnalysis(
      signatories = PartyExpressionAnalyzer.analyze(tmpl.signatories, tmpl.param, resolveValue, currentPackageId),
      observers = PartyExpressionAnalyzer.analyze(tmpl.observers, tmpl.param, resolveValue, currentPackageId),
      choices = tmpl.choices.map { case (name, choice) =>
        name -> analyseChoice(choice, tmpl.param, currentPackageId, resolveValue)
      },
    )

  private def analyseChoice(
      choice: Ast.TemplateChoice,
      templateParam: String,
      currentPackageId: PackageId,
      resolveValue: PartyExpressionAnalyzer.ValueResolver,
  ): ChoicePartyAnalysis =
    ChoicePartyAnalysis(
      controllers = PartyExpressionAnalyzer.analyze(choice.controllers, templateParam, resolveValue, currentPackageId),
      observers = choice.choiceObservers
        .map(PartyExpressionAnalyzer.analyze(_, templateParam, resolveValue, currentPackageId))
        .getOrElse(PartyAnalysisResult.Static(Nil)),
    )
}
