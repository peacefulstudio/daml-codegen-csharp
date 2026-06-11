// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import com.digitalasset.daml.lf.language.Ast

import scala.collection.mutable

/** Outcome of analysing a `List Party`-typed Daml-LF expression.
  *
  * `Static(payloadFields)` means every party in the list was resolvable
  * to a payload-field projection on the template parameter and the
  * fields are listed in declaration order. An empty list is a valid
  * Static verdict (the Daml source was a literal `[]`).
  *
  * `Dynamic` means the analyser could not statically resolve at least
  * one element of the list — function applications, key projections,
  * projections through a let binding all collapse here. Codegen surfaces
  * Dynamic as an explicit `SubmitterInfo` parameter rather than
  * inventing an `actAs` set from an unverified expression shape.
  */
sealed trait PartyAnalysisResult extends Product with Serializable
object PartyAnalysisResult {
  final case class Static(payloadFields: List[String]) extends PartyAnalysisResult
  case object Dynamic                                  extends PartyAnalysisResult
}

/** Scala port of `Daml.Codegen.DarParser.PartyExpressionAnalyzer` (C#)
  * adapted to the `daml-lf-archive` `Ast.Expr` shape rather than the raw
  * Daml-LF protobuf. The analyser recognises the LF emission pattern
  * Daml uses for the `signatory party1, party2, ...` surface syntax:
  * a `Cons` chain whose head expressions are `ERecProj(_, fieldName,
  * EVar(templateParam))` nodes and whose tail terminates in `ENil`.
  * Anything else — function calls, list helpers, let bindings, key
  * projections — short-circuits to [[PartyAnalysisResult.Dynamic]].
  *
  * Per ADR 0003 (amendment 2026-05-27) this runs as a side-car pass
  * over the full-decode `Ast.Package` map, before [[SignatureErasure]]
  * drops the expression bodies, and emits its verdict into the
  * `IntermediateDar` proto's `PartyAnalysis` message.
  */
object PartyExpressionAnalyzer {

  /** Analyses an `Ast.Expr` rooted at a `List Party`-typed value. Returns
    * [[PartyAnalysisResult.Dynamic]] for any shape outside the
    * `Cons`-of-`ERecProj`-on-`templateParam` pattern (or when `expr` is
    * null, defensively).
    */
  def analyze(expr: Ast.Expr, templateParam: String): PartyAnalysisResult = {
    if (expr == null) return PartyAnalysisResult.Dynamic
    val collected = mutable.ListBuffer.empty[String]
    if (tryCollect(expr, templateParam, collected)) PartyAnalysisResult.Static(collected.toList)
    else PartyAnalysisResult.Dynamic
  }

  private def tryCollect(
      expr: Ast.Expr,
      templateParam: String,
      acc: mutable.ListBuffer[String],
  ): Boolean = expr match {
    case _: Ast.ENil =>
      true

    case Ast.ECons(_, front, tail) =>
      front.toSeq.forall(tryResolveSingleParty(_, templateParam, acc)) &&
        tryCollect(tail, templateParam, acc)

    case _ => false
  }

  private def tryResolveSingleParty(
      expr: Ast.Expr,
      templateParam: String,
      acc: mutable.ListBuffer[String],
  ): Boolean = expr match {
    case Ast.ERecProj(_, field, Ast.EVar(record)) if record.toString == templateParam =>
      acc += field.toString
      true

    case _ => false
  }
}
