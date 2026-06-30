// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import studio.peaceful.daml.codegen.intermediate.intermediate_dar.IntermediateDar

import java.io.{BufferedOutputStream, FileOutputStream, IOException, OutputStream}
import java.nio.file.{Files, Path, Paths}

/** Entry point for the JVM codegen helper.
  *
  * Reads a Daml `.dar` archive using `daml-lf-archive`, translates it via
  * [[AstToIntermediate]], and writes an `IntermediateDar` protobuf message
  * to the output path supplied on the command line. The default path is
  * full-decode + static party-expression analysis ([[FullDecoder]] +
  * [[PartyExpressionAnalyzer]] + [[PartyAnalyses]]), with
  * [[SignatureErasure]] applied after analysis so the proto carries only
  * data signatures plus the analyzer's verdicts. The `--schema-only`
  * opt-out routes through [[SchemaDecoder]] instead and emits `Dynamic`
  * everywhere — patch-version-insensitive but no typed-`actAs`. See ADR
  * 0003 (amendment 2026-05-27).
  *
  * This object stays free of any `daml-lf-archive` Scala case-class shapes.
  * All coupling to upstream DA AST types lives in [[AstToIntermediate]],
  * [[SignatureErasure]], [[SchemaDecoder]], [[FullDecoder]],
  * [[PartyExpressionAnalyzer]], and [[PartyAnalyses]] — if DA rebases its
  * AST, the changes land in those files.
  */
object Decode {

  private val Usage: String =
    """Usage: daml-codegen-jvm-helper --dar <path-to.dar> --out <path-to-output.binpb> [--schema-only]
      |
      |Options:
      |  --schema-only  Opt into the patch-version-insensitive schema-mode decode path.
      |                 The default is full-decode + static party-expression analysis,
      |                 which is patch-version-sensitive but enables typed-actAs codegen
      |                 (see ADR 0003 amendment, 2026-05-27).""".stripMargin

  def main(args: Array[String]): Unit = sys.exit(runCli(args.toList))

  /** Drives the CLI end-to-end and returns the process exit code.
    *
    *   - `0` — success (or `--help`)
    *   - `1` — runtime failure (DAR read, decode, or write)
    *   - `2` — CLI usage error (unknown arg, missing required arg)
    *
    * Side-effecting on stdout/stderr; visible for testing so tests need not
    * subprocess the JAR.
    */
  def runCli(args: List[String]): Int =
    parseArgs(args) match {
      case Right(CliCommand.Help) =>
        println(Usage)
        0
      case Right(CliCommand.Run(parsed)) =>
        run(parsed) match {
          case Right(()) => 0
          case Left(message) =>
            System.err.println(message)
            1
        }
      case Left(message) =>
        System.err.println(message)
        System.err.println(Usage)
        2
    }

  /** Reads the DAR at `args.darPath`, translates it to an `IntermediateDar`,
    * and writes the binary proto to `args.outPath`. Visible for testing.
    *
    * `args.schemaOnly == false` (the default) runs full-decode +
    * [[PartyExpressionAnalyzer]] before erasing expression bodies, so
    * the emitted proto carries `Static`/`Dynamic` verdicts on each
    * template / choice. `args.schemaOnly == true` skips the analyser
    * and the proto carries `Dynamic` everywhere — patch-version-insensitive
    * but loses the typed-actAs codegen path. See ADR 0003 (amendment
    * 2026-05-27).
    */
  def run(args: ParsedArgs): Either[String, Unit] =
    for {
      proto <- if (args.schemaOnly) decodeSchemaOnly(args) else decodeFull(args)
      _     <- writeProto(args.outPath, proto)
    } yield ()

  private def decodeSchemaOnly(args: ParsedArgs)
      : Either[String, studio.peaceful.daml.codegen.intermediate.intermediate_dar.IntermediateDar] =
    SchemaDecoder
      .readDar(args.darPath.toFile)
      .map(signatures => AstToIntermediate.translate(signatures, PartyAnalyses.empty))

  private def decodeFull(args: ParsedArgs)
      : Either[String, studio.peaceful.daml.codegen.intermediate.intermediate_dar.IntermediateDar] =
    FullDecoder
      .readDar(args.darPath.toFile)
      .map { fullDar =>
        val analyses = PartyAnalyses.compute(fullDar)
        AstToIntermediate.translate(SignatureErasure.eraseDar(fullDar), analyses)
      }

  private def writeProto(out: Path, proto: IntermediateDar): Either[String, Unit] = {
    try {
      val parent = out.getParent
      if (parent != null) Files.createDirectories(parent)
      val stream = new BufferedOutputStream(new FileOutputStream(out.toFile))
      writeProtoToStream(stream, proto).left.map(message =>
        s"Failed to write proto to $out: $message"
      )
    } catch {
      case e: IOException => Left(s"Failed to write proto to $out: ${e.getMessage}")
    }
  }

  /** Writes `proto` to `stream` and closes the stream best-effort.
    *
    * Visible for testing. A `close()` failure on a stream whose write
    * already succeeded is logged-only — the write itself is the
    * meaningful unit of success, and a successful write that the OS
    * then fails to release a handle for is still a successful write.
    * A write failure overrides a close failure (write errors are
    * reported even if close also throws).
    */
  def writeProtoToStream(stream: OutputStream, proto: IntermediateDar): Either[String, Unit] = {
    var writeFailure: Option[String] = None
    try {
      proto.writeTo(stream)
      stream.flush()
    } catch {
      case e: IOException => writeFailure = Some(e.getMessage)
    }
    try stream.close()
    catch { case _: IOException => () }
    writeFailure match {
      case Some(message) => Left(message)
      case None          => Right(())
    }
  }

  /** Parsed command-line arguments. */
  final case class ParsedArgs(darPath: Path, outPath: Path, schemaOnly: Boolean = false)

  /** Outcome of parsing CLI args: either run with parsed args, or print help. */
  sealed trait CliCommand
  object CliCommand {
    final case class Run(args: ParsedArgs) extends CliCommand
    case object Help extends CliCommand
  }

  /** Parses `--dar <path> --out <path>` (or `--help`) from a list of CLI
    * arguments. `--help` is a distinct successful outcome, not an error,
    * so that downstream tooling probing `--help` sees exit code 0.
    */
  def parseArgs(args: List[String]): Either[String, CliCommand] = {
    if (args.contains("--help")) Right(CliCommand.Help)
    else {
      @scala.annotation.tailrec
      def loop(
          remaining: List[String],
          dar: Option[Path],
          out: Option[Path],
          schemaOnly: Boolean,
      ): Either[String, CliCommand] = remaining match {
        case Nil =>
          (dar, out) match {
            case (Some(d), Some(o)) => Right(CliCommand.Run(ParsedArgs(d, o, schemaOnly)))
            case (None, _)          => Left("Missing required argument: --dar")
            case (_, None)          => Left("Missing required argument: --out")
          }
        case "--dar" :: value :: rest => loop(rest, Some(Paths.get(value)), out, schemaOnly)
        case "--out" :: value :: rest => loop(rest, dar, Some(Paths.get(value)), schemaOnly)
        case "--schema-only" :: rest  => loop(rest, dar, out, schemaOnly = true)
        case other :: _               => Left(s"Unrecognised argument: $other")
      }
      loop(args, None, None, schemaOnly = false)
    }
  }
}
