// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import com.digitalasset.daml.lf.archive.Dar
import com.digitalasset.daml.lf.data.Ref.{ChoiceName, DottedName, PackageId}
import com.digitalasset.daml.lf.language.Ast

/** Per-template static analysis verdicts, computed against a fully-decoded
  * `Ast.Package` (i.e. before `SignatureErasure`). Both template-level
  * (`signatories`, `observers`) and per-choice (`controllers`,
  * `choiceObservers`) verdicts are carried; the missing-key default is
  * `Dynamic` so this map can be empty when the JVM helper runs in
  * `--schema-only` mode and the proto carries `Dynamic` everywhere.
  */
final case class TemplatePartyAnalysis(
    signatories: PartyAnalysisResult,
    observers: PartyAnalysisResult,
    choices: Map[ChoiceName, ChoicePartyAnalysis],
)

object TemplatePartyAnalysis {

  /** The all-Dynamic verdict — used as the lookup default when no
    * analysis was recorded for a template (the helper ran in
    * `--schema-only` mode, or the template lookup missed).
    */
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

  /** The all-Dynamic verdict — used as the default when no analysis was
    * recorded for a choice (either because the helper ran in
    * `--schema-only` mode, or because the choice belongs to an interface
    * rather than a template, or because the analyser short-circuited).
    */
  val dynamic: ChoicePartyAnalysis =
    ChoicePartyAnalysis(PartyAnalysisResult.Dynamic, PartyAnalysisResult.Dynamic)
}

/** Lookup table keyed by `(packageId, moduleName, templateName)` from
  * [[PartyAnalyses.compute]] over a fully-decoded DAR. Returned as a
  * plain `Map` for trivially-deterministic iteration; `AstToIntermediate`
  * looks up entries by key during translation.
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
    * choices (`mod.interfaces` / `Ast.DefInterface.choices`) and interface
    * implementations on templates (`tmpl.implements`) are intentionally NOT
    * analysed — [[AstToIntermediate.translateInterface]] stamps
    * [[ChoicePartyAnalysis.dynamic]] on every interface choice. Adding
    * typed-`actAs` derivation for interface choices is a deliberate
    * follow-up. The output is a map keyed by
    * `(packageId, moduleName, templateName)`.
    */
  def compute(dar: Dar[(PackageId, Ast.Package)]): PartyAnalyses = {
    val builder = Map.newBuilder[(PackageId, DottedName, DottedName), TemplatePartyAnalysis]
    (dar.main +: dar.dependencies).foreach { case (packageId, pkg) =>
      pkg.modules.foreach { case (modName, mod) =>
        mod.templates.foreach { case (tmplName, tmpl) =>
          builder += ((packageId, modName, tmplName) -> analyseTemplate(tmpl))
        }
      }
    }
    PartyAnalyses(builder.result())
  }

  private def analyseTemplate(tmpl: Ast.Template): TemplatePartyAnalysis =
    TemplatePartyAnalysis(
      signatories = PartyExpressionAnalyzer.analyze(tmpl.signatories, tmpl.param),
      observers = PartyExpressionAnalyzer.analyze(tmpl.observers, tmpl.param),
      choices = tmpl.choices.map { case (name, choice) =>
        name -> analyseChoice(choice, tmpl.param)
      },
    )

  private def analyseChoice(
      choice: Ast.TemplateChoice,
      templateParam: String,
  ): ChoicePartyAnalysis =
    ChoicePartyAnalysis(
      controllers = PartyExpressionAnalyzer.analyze(choice.controllers, templateParam),
      observers = choice.choiceObservers
        .map(PartyExpressionAnalyzer.analyze(_, templateParam))
        .getOrElse(PartyAnalysisResult.Static(Nil)),
    )
}
