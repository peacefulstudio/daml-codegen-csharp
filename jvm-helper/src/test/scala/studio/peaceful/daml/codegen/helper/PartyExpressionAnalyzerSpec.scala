// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import com.digitalasset.daml.lf.data.{ImmArray, Ref}
import com.digitalasset.daml.lf.language.Ast

import org.scalatest.matchers.should.Matchers
import org.scalatest.wordspec.AnyWordSpec

/** Scala port of `PartyExpressionAnalyzerTests` (C# side, in
  * `tests/Daml.Codegen.CSharp.Tests/PartyExpressionAnalyzerTests.cs`). The
  * analyzer walks a Daml-LF `Ast.Expr` rooted at a `List Party`-typed value
  * (the shape used for template `signatories` / `observers` and choice
  * `controllers` / `choiceObservers`) and decides whether it can be mapped
  * to an ordered list of payload-field references.
  *
  * Shapes recognised as static: `ECons` chains whose front elements are
  * `ERecProj(_, field, EVar(templateParam))` and whose tail terminates in
  * `ENil`. Any other shape — function application, projection through a
  * different binder, key-derived parties — short-circuits to
  * [[PartyAnalysisResult.Dynamic]].
  */
class PartyExpressionAnalyzerSpec extends AnyWordSpec with Matchers {

  private val TParty: Ast.Type = Ast.TBuiltin(Ast.BTParty)
  private val PartyList: Ast.Type = Ast.TApp(Ast.TBuiltin(Ast.BTList), TParty)
  private val ThisBinder: String = "this"
  private val SomeTycon: Ast.TypeConApp =
    Ast.TypeConApp(
      tycon = Ref.Identifier.assertFromString(
        "0000000000000000000000000000000000000000000000000000000000000001:Mod:Tpl"
      ),
      args = ImmArray.empty,
    )

  private def recProjOnThis(field: String, binder: String = ThisBinder): Ast.Expr =
    Ast.ERecProj(
      tycon = SomeTycon,
      field = Ref.Name.assertFromString(field),
      record = Ast.EVar(Ref.Name.assertFromString(binder)),
    )

  private def consList(heads: Ast.Expr*): Ast.Expr =
    Ast.ECons(
      typ = PartyList,
      front = ImmArray(heads: _*),
      tail = Ast.ENil(PartyList),
    )

  "PartyExpressionAnalyzer.analyze" should {

    "resolve a single payload-field signatory to Static" in {
      val expr = consList(recProjOnThis("platform"))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder) shouldBe
        PartyAnalysisResult.Static(List("platform"))
    }

    "resolve multi-payload-field signatory in declaration order" in {
      val expr = consList(
        recProjOnThis("platform"),
        recProjOnThis("initiator"),
        recProjOnThis("counterparty"),
      )
      PartyExpressionAnalyzer.analyze(expr, ThisBinder) shouldBe
        PartyAnalysisResult.Static(List("platform", "initiator", "counterparty"))
    }

    "return Static empty for a literal empty list" in {
      PartyExpressionAnalyzer.analyze(Ast.ENil(PartyList), ThisBinder) shouldBe
        PartyAnalysisResult.Static(Nil)
    }

    "return Dynamic when projection root is not the template parameter" in {
      val expr = consList(recProjOnThis("owner", binder = "x"))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder) shouldBe PartyAnalysisResult.Dynamic
    }

    "return Dynamic for a function application" in {
      val app = Ast.EApp(
        fun = Ast.EVar(Ref.Name.assertFromString("helper")),
        arg = Ast.EVar(Ref.Name.assertFromString(ThisBinder)),
      )
      PartyExpressionAnalyzer.analyze(app, ThisBinder) shouldBe PartyAnalysisResult.Dynamic
    }

    "return Dynamic when one element of a list is dynamic" in {
      val good = recProjOnThis("platform")
      val bad = Ast.EApp(
        fun = Ast.EVar(Ref.Name.assertFromString("mysteryFn")),
        arg = Ast.EVar(Ref.Name.assertFromString(ThisBinder)),
      )
      val expr = consList(good, bad)
      PartyExpressionAnalyzer.analyze(expr, ThisBinder) shouldBe PartyAnalysisResult.Dynamic
    }

    "return Dynamic for projection through the choice-argument binder" in {
      val expr = consList(recProjOnThis("requester", binder = "arg"))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder) shouldBe PartyAnalysisResult.Dynamic
    }

    "return Dynamic when the Cons tail is not a Nil or another Cons" in {
      val malformedTail = Ast.EVar(Ref.Name.assertFromString("xs"))
      val expr = Ast.ECons(
        typ = PartyList,
        front = ImmArray(recProjOnThis("platform")),
        tail = malformedTail,
      )
      PartyExpressionAnalyzer.analyze(expr, ThisBinder) shouldBe PartyAnalysisResult.Dynamic
    }
  }
}
