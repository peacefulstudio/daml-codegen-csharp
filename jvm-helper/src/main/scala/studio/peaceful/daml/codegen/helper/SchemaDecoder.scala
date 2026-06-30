// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import com.digitalasset.daml.lf.archive.{ArchivePayload, Dar, Decode => LfDecode}
import com.digitalasset.daml.lf.data.Ref.PackageId
import com.digitalasset.daml.lf.language.Ast

import java.io.File

/** Reads a DAR file in schema mode (serializable data only, no expression
  * bodies).
  *
  * Uses `decodeArchivePayloadSchema` from `daml-lf-archive`, which strips
  * non-serializable definitions and erases expression bodies, yielding an
  * `Ast.PackageSignature` (i.e. `Ast.GenPackage[Unit]`).
  * Two patch-different versions of the same package decode to identical
  * `PackageSignature` values, which is load-bearing for the 4-part NuGet
  * versioning per ADR 0002.
  */
object SchemaDecoder {

  def readDar(
      darFile: File
  ): Either[String, Dar[(PackageId, Ast.PackageSignature)]] =
    DarDecoders.readArchiveAndMap(darFile, decodePayload)

  private def decodePayload(
      payload: ArchivePayload
  ): Either[String, (PackageId, Ast.PackageSignature)] =
    LfDecode
      .decodeArchivePayloadSchema(payload)
      .left
      .map(err => s"Failed to decode archive payload ${payload.pkgId}: $err")
}
