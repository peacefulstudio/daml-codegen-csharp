// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import com.digitalasset.daml.lf.data.Ref
import com.digitalasset.daml.lf.language.Ast

import scala.annotation.tailrec
import scala.collection.mutable

/** Verdict for a `List Party`-typed Daml-LF expression.
  *
  * [[PartyAnalysisResult.Static]] carries the template payload fields, in
  * declaration order, that every element of the list projects from the
  * template parameter; the empty list is a valid Static verdict.
  * [[PartyAnalysisResult.Dynamic]] means at least one element could not be
  * resolved to such a projection.
  */
sealed trait PartyAnalysisResult extends Product with Serializable
object PartyAnalysisResult {
  final case class Static(payloadFields: List[String]) extends PartyAnalysisResult
  case object Dynamic                                  extends PartyAnalysisResult
}

/** Classifies a `List Party`-typed Daml-LF expression as a
  * [[PartyAnalysisResult.Static]] list of template payload-field projections
  * or as [[PartyAnalysisResult.Dynamic]].
  */
object PartyExpressionAnalyzer {

  type ValueResolver = Ref.ValueRef => Option[Ast.Expr]

  def analyze(
      expr: Ast.Expr,
      templateParam: String,
      resolveValue: ValueResolver,
      currentPackageId: Ref.PackageId,
  ): PartyAnalysisResult = {
    if (expr == null) return PartyAnalysisResult.Dynamic
    val payloadFields = mutable.ListBuffer.empty[String]
    if (
      collectPayloadFields(
        expr,
        templateParam,
        payloadFields,
        resolveValue,
        currentPackageId,
        throughToPartiesIndirection = true,
      )
    )
      PartyAnalysisResult.Static(payloadFields.toList)
    else PartyAnalysisResult.Dynamic
  }

  private def collectPayloadFields(
      expr: Ast.Expr,
      templateParam: String,
      acc: mutable.ListBuffer[String],
      resolveValue: ValueResolver,
      currentPackageId: Ref.PackageId,
      throughToPartiesIndirection: Boolean,
  ): Boolean = expr match {
    case _: Ast.ENil =>
      true

    case Ast.ECons(_, front, tail) =>
      front.toSeq.forall(collectPayloadFieldProjection(_, templateParam, acc)) &&
        collectPayloadFields(tail, templateParam, acc, resolveValue, currentPackageId, throughToPartiesIndirection)

    case Ast.EApp(callee, Ast.EVar(arg))
        if throughToPartiesIndirection && arg.toString == templateParam =>
      resolveArity1ValueBody(callee, resolveValue) match {
        case Some((binder, body)) =>
          collectPayloadFields(body, binder, acc, resolveValue, currentPackageId, throughToPartiesIndirection = false)
        case None => false
      }

    case app: Ast.EApp if throughToPartiesIndirection =>
      resolveChoicePartyIndirection(app, templateParam, acc, resolveValue, currentPackageId)

    case _ => false
  }

  private sealed trait PartyBinding
  private object PartyBinding {
    case object Opaque                           extends PartyBinding
    case object TemplateParam                    extends PartyBinding
    final case class PayloadField(field: String) extends PartyBinding
  }

  /** Resolves the per-choice `controller <field>` / `observer <field>`
    * indirection this SDK compiles to a two-or-more-argument application of a
    * synthesized value to the template parameter and the choice argument —
    * `App(Val(<self>), [this, arg])` — whose defining expression reduces to
    * `\this -> let ds = this.<field> in \arg -> toParties ds`. The extra
    * choice-argument binder distinguishes it from the arity-1 template-level
    * signatory/observer indirection, which stays Dynamic when it chains through
    * `toParties`. Only the imported single-`Party` list wrapper
    * `DA.Internal.Template.Functions.toParties` applied to a bound template
    * payload field resolves; every other shape stays Dynamic.
    */
  private def resolveChoicePartyIndirection(
      app: Ast.EApp,
      templateParam: String,
      acc: mutable.ListBuffer[String],
      resolveValue: ValueResolver,
      currentPackageId: Ref.PackageId,
  ): Boolean = {
    val (head, args) = flattenApplication(app)
    if (args.length < 2) return false
    args.head match {
      case Ast.EVar(contractArg) if contractArg.toString == templateParam =>
        unwrapTypeApplications(head) match {
          case Ast.EVal(ref) =>
            resolveValue(ref) match {
              case Some(Ast.EAbs((binder, _), body)) =>
                val environment =
                  mutable.Map[String, PartyBinding](binder.toString -> PartyBinding.TemplateParam)
                reduceSingletonPartyField(body, environment, currentPackageId) match {
                  case Some(field) => acc += field; true
                  case None        => false
                }
              case _ => false
            }
          case _ => false
        }
      case _ => false
    }
  }

  private def flattenApplication(expr: Ast.Expr): (Ast.Expr, List[Ast.Expr]) = {
    @tailrec
    def go(e: Ast.Expr, acc: List[Ast.Expr]): (Ast.Expr, List[Ast.Expr]) = e match {
      case Ast.EApp(fun, arg) => go(fun, arg :: acc)
      case other              => (other, acc)
    }
    go(expr, Nil)
  }

  private def reduceSingletonPartyField(
      expr: Ast.Expr,
      environment: mutable.Map[String, PartyBinding],
      currentPackageId: Ref.PackageId,
  ): Option[String] = expr match {
    case Ast.ELet(binding, body) =>
      binding.binder.foreach(name =>
        environment(name.toString) = resolvePartyBinding(binding.bound, environment)
      )
      reduceSingletonPartyField(body, environment, currentPackageId)

    case Ast.EAbs((binder, _), body) =>
      environment(binder.toString) = PartyBinding.Opaque
      reduceSingletonPartyField(body, environment, currentPackageId)

    case Ast.EApp(callee, arg) =>
      unwrapTypeApplications(callee) match {
        case Ast.EVal(ref) if isImportedToParties(ref, currentPackageId) =>
          resolvePartyBinding(arg, environment) match {
            case PartyBinding.PayloadField(field) => Some(field)
            case _                                => None
          }
        case _ => None
      }

    case _ => None
  }

  private def resolvePartyBinding(
      expr: Ast.Expr,
      environment: mutable.Map[String, PartyBinding],
  ): PartyBinding = expr match {
    case Ast.EVar(name) =>
      environment.getOrElse(name.toString, PartyBinding.Opaque)

    case Ast.ERecProj(_, field, Ast.EVar(record))
        if environment.get(record.toString).contains(PartyBinding.TemplateParam) =>
      PartyBinding.PayloadField(field.toString)

    case _ => PartyBinding.Opaque
  }

  private def isImportedToParties(ref: Ref.ValueRef, currentPackageId: Ref.PackageId): Boolean =
    ref.packageId != currentPackageId &&
      ref.qualifiedName.module.toString == "DA.Internal.Template.Functions" &&
      ref.qualifiedName.name.toString.endsWith("toParties")

  private def resolveArity1ValueBody(
      callee: Ast.Expr,
      resolveValue: ValueResolver,
  ): Option[(String, Ast.Expr)] =
    unwrapTypeApplications(callee) match {
      case Ast.EVal(ref) =>
        resolveValue(ref).collect { case Ast.EAbs((binder, _), body) => (binder.toString, body) }
      case _ => None
    }

  @tailrec
  private def unwrapTypeApplications(expr: Ast.Expr): Ast.Expr = expr match {
    case Ast.ETyApp(inner, _) => unwrapTypeApplications(inner)
    case other                => other
  }

  private def collectPayloadFieldProjection(
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
