// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import com.digitalasset.daml.lf.archive.{ArchivePayload, Dar, DarReader => LfDarReader}

import java.io.File

/** Shared DAR-reading boilerplate factored out of [[SchemaDecoder]] and
  * [[FullDecoder]]. Both decoders read the archive via
  * `LfDarReader.readArchiveFromFile`, decode each `ArchivePayload`, and
  * assemble a `Dar[A]` — they differ only in the per-payload decode
  * step and (in `SchemaDecoder`'s case) the post-decode erasure.
  */
private[helper] object DarDecoders {

  def readArchiveAndMap[A](
      darFile: File,
      decodePayload: ArchivePayload => Either[String, A],
  ): Either[String, Dar[A]] =
    LfDarReader.readArchiveFromFile(darFile) match {
      case Left(error) => Left(s"Failed to read DAR ${darFile.getName}: $error")
      case Right(dar)  =>
        for {
          main <- decodePayload(dar.main)
          deps <- foldDecodes(dar.dependencies, decodePayload)
        } yield Dar(main, deps)
    }

  private def foldDecodes[A](
      payloads: List[ArchivePayload],
      decodePayload: ArchivePayload => Either[String, A],
  ): Either[String, List[A]] = {
    val builder = List.newBuilder[A]
    val iter    = payloads.iterator
    while (iter.hasNext) {
      decodePayload(iter.next()) match {
        case Right(value) => builder += value
        case Left(error)  => return Left(error)
      }
    }
    Right(builder.result())
  }
}
