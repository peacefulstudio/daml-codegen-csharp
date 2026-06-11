// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import com.digitalasset.daml.lf.archive.{ArchivePayload, Dar, Decode => LfDecode}
import com.digitalasset.daml.lf.data.Ref.PackageId
import com.digitalasset.daml.lf.language.Ast

import java.io.File

/** Reads a DAR file in full-decode mode. `decodeArchivePayload` returns all
  * definitions (serializable and non-serializable); [[SignatureErasure.erasePackage]]
  * strips non-serializable `DDataType` entries after expression analysis,
  * keeping the two decode paths structurally equivalent. Template and choice
  * expression bodies — `signatories`, `observers`, `controllers`,
  * `choiceObservers` — are kept intact so that downstream party-expression
  * analysis can run against the actual `Ast.Expr`. The only behavioural
  * difference vs. [[SchemaDecoder]] is that this path does NOT apply
  * [[SignatureErasure.erasePackage]] itself — that step is applied by the
  * caller in `Decode.decodeFull`.
  *
  * Full-decode is the default path on the proto pipeline per the ADR 0003
  * amendment (2026-05-27). The `--schema-only` opt-out routes through
  * [[SchemaDecoder]] instead — that path is patch-version-insensitive but
  * gives up static `actAs` derivation. Switching defaults to full-decode
  * means generated code is patch-version-sensitive by default; consumers
  * who need patch-insensitive decode should pass `--schema-only`.
  */
object FullDecoder {

  def readDar(darFile: File): Either[String, Dar[(PackageId, Ast.Package)]] =
    DarDecoders.readArchiveAndMap(darFile, decodePayload)

  private def decodePayload(
      payload: ArchivePayload
  ): Either[String, (PackageId, Ast.Package)] =
    LfDecode
      .decodeArchivePayload(payload)
      .left
      .map(err => s"Failed to decode archive payload ${payload.pkgId}: $err")
}
