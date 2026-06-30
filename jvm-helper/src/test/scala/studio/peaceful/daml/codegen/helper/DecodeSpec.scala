// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import studio.peaceful.daml.codegen.intermediate.intermediate_dar.{
  IntermediateDar,
  IntermediatePackage,
}

import org.scalatest.{EitherValues, Inspectors, OptionValues}
import org.scalatest.matchers.should.Matchers
import org.scalatest.wordspec.AnyWordSpec

import java.io.{IOException, OutputStream}
import java.nio.file.{Files, Paths}

class DecodeSpec extends AnyWordSpec with Matchers with EitherValues with OptionValues with Inspectors {

  private val FixtureDar = Paths.get(
    sys.props.getOrElse("jvmHelper.testFixtureDar",
      "../tests/Daml.Codegen.CSharp.Tests/Snapshots/splice-api-token-holding-v1/splice-api-token-holding-v1.dar"
    )
  )

  private def nonEmptyProto: IntermediateDar =
    IntermediateDar(main = Some(IntermediatePackage(packageId = "abc", packageName = "p")))

  "Decode" should {

    "parse --dar and --out arguments" in {
      Decode.parseArgs(List("--dar", "a.dar", "--out", "b.binpb")) match {
        case Right(Decode.CliCommand.Run(args)) =>
          args.darPath.toString shouldBe "a.dar"
          args.outPath.toString  shouldBe "b.binpb"
          args.schemaOnly        shouldBe false
        case other => fail(s"expected Run, got $other")
      }
    }

    "default schemaOnly to false (full-decode is the default per ADR 0003 amendment)" in {
      Decode.parseArgs(List("--dar", "a.dar", "--out", "b.binpb")) match {
        case Right(Decode.CliCommand.Run(args)) => args.schemaOnly shouldBe false
        case other => fail(s"expected Run, got $other")
      }
    }

    "parse --schema-only as an opt-out into schema-mode decode" in {
      Decode.parseArgs(List("--dar", "a.dar", "--out", "b.binpb", "--schema-only")) match {
        case Right(Decode.CliCommand.Run(args)) => args.schemaOnly shouldBe true
        case other => fail(s"expected Run, got $other")
      }
    }

    "reject missing --dar" in {
      Decode.parseArgs(List("--out", "b.binpb")) shouldBe Left("Missing required argument: --dar")
    }

    "reject missing --out" in {
      Decode.parseArgs(List("--dar", "a.dar")) shouldBe Left("Missing required argument: --out")
    }

    "reject unknown arguments" in {
      Decode.parseArgs(List("--bogus")).left.toOption.value should startWith("Unrecognised argument")
    }

    "recognise --help as a distinct request, not an error" in {
      Decode.parseArgs(List("--help")) shouldBe Right(Decode.CliCommand.Help)
    }

    "exit with code 0 on --help" in {
      Decode.runCli(List("--help")) shouldBe 0
    }

    "exit with code 2 on parse error" in {
      Decode.runCli(List("--bogus")) shouldBe 2
    }

    "write a non-empty IntermediateDar binary to disk for the fixture DAR" in {
      Files.exists(FixtureDar) shouldBe true
      val outPath = Files.createTempFile("intermediate-dar-", ".binpb")
      try {
        Decode.run(Decode.ParsedArgs(FixtureDar, outPath)) shouldBe Right(())
        Files.exists(outPath) shouldBe true
        Files.size(outPath) should be > 0L
        val proto = IntermediateDar.parseFrom(Files.readAllBytes(outPath))
        proto.main.value.packageId should not be empty
        proto.main.value.modules should not be empty
      } finally {
        Files.deleteIfExists(outPath)
      }
    }

    "default decode populates the signatories field on every template" in {
      Files.exists(FixtureDar) shouldBe true
      val outPath = Files.createTempFile("intermediate-dar-default-", ".binpb")
      try {
        Decode.run(Decode.ParsedArgs(FixtureDar, outPath, schemaOnly = false)) shouldBe Right(())
        val proto = IntermediateDar.parseFrom(Files.readAllBytes(outPath))
        val allTemplates = (proto.main.toSeq ++ proto.dependencies).flatMap(_.modules).flatMap(_.templates)
        forAll(allTemplates) { tmpl =>
          tmpl.signatories shouldBe defined
          tmpl.observers shouldBe defined
        }
      } finally {
        Files.deleteIfExists(outPath)
      }
    }

    "--schema-only decode produces Dynamic party analysis on every template" in {
      Files.exists(FixtureDar) shouldBe true
      val outPath = Files.createTempFile("intermediate-dar-schemaonly-", ".binpb")
      try {
        Decode.run(Decode.ParsedArgs(FixtureDar, outPath, schemaOnly = true)) shouldBe Right(())
        val proto = IntermediateDar.parseFrom(Files.readAllBytes(outPath))
        val mainModules = proto.main.value.modules
        val templates = mainModules.flatMap(_.templates)
        forAll(templates) { tmpl =>
          tmpl.signatories.value.shape.isDynamic shouldBe true
          tmpl.observers.value.shape.isDynamic shouldBe true
          forAll(tmpl.choices) { ch =>
            ch.controllers.value.shape.isDynamic shouldBe true
            ch.observers.value.shape.isDynamic shouldBe true
          }
        }
      } finally {
        Files.deleteIfExists(outPath)
      }
    }

    "default and --schema-only decode produce identical non-party content on the fixture DAR" in {
      Files.exists(FixtureDar) shouldBe true
      val fullOut   = Files.createTempFile("intermediate-dar-full-", ".binpb")
      val schemaOut = Files.createTempFile("intermediate-dar-schema-", ".binpb")
      try {
        Decode.run(Decode.ParsedArgs(FixtureDar, fullOut, schemaOnly = false))   shouldBe Right(())
        Decode.run(Decode.ParsedArgs(FixtureDar, schemaOut, schemaOnly = true)) shouldBe Right(())
        val full   = IntermediateDar.parseFrom(Files.readAllBytes(fullOut))
        val schema = IntermediateDar.parseFrom(Files.readAllBytes(schemaOut))
        val fullStripped   = stripPartyAnalyses(full)
        val schemaStripped = stripPartyAnalyses(schema)
        fullStripped.toByteArray.toSeq shouldBe schemaStripped.toByteArray.toSeq
      } finally {
        Files.deleteIfExists(fullOut)
        Files.deleteIfExists(schemaOut)
      }
    }

    "treat a successful write as success even when stream.close() throws" in {
      val written = new java.io.ByteArrayOutputStream()
      val closeAfterSuccessfulWrite = new OutputStream {
        override def write(b: Int): Unit = written.write(b)
        override def write(b: Array[Byte], off: Int, len: Int): Unit = written.write(b, off, len)
        override def flush(): Unit = ()
        override def close(): Unit = throw new IOException("simulated close failure")
      }
      val proto = nonEmptyProto
      Decode.writeProtoToStream(closeAfterSuccessfulWrite, proto) shouldBe Right(())
      written.toByteArray.length shouldBe proto.serializedSize
    }

    "report a write failure even if stream.close() also throws" in {
      val failingWrite = new OutputStream {
        override def write(b: Int): Unit = throw new IOException("simulated write failure")
        override def write(b: Array[Byte], off: Int, len: Int): Unit =
          throw new IOException("simulated write failure")
        override def flush(): Unit = ()
        override def close(): Unit = throw new IOException("simulated close failure")
      }
      Decode.writeProtoToStream(failingWrite, nonEmptyProto) match {
        case Left(message) => (message: String) should include("simulated write failure")
        case Right(())     => fail("expected Left on write failure")
      }
    }

    "exit with code 0 when runCli drives a successful end-to-end DAR translation" in {
      Files.exists(FixtureDar) shouldBe true
      val outPath = Files.createTempFile("intermediate-dar-runcli-ok-", ".binpb")
      try {
        Decode.runCli(List(
          "--dar", FixtureDar.toString,
          "--out", outPath.toString,
        )) shouldBe 0
        Files.size(outPath) should be > 0L
      } finally {
        Files.deleteIfExists(outPath)
      }
    }

    "exit with code 1 when runCli fails to read a non-existent DAR" in {
      val missingDar = Paths.get("/nonexistent-dar-path-for-runcli-test.dar")
      val outPath    = Files.createTempFile("intermediate-dar-runcli-err-", ".binpb")
      try {
        Decode.runCli(List(
          "--dar", missingDar.toString,
          "--out", outPath.toString,
        )) shouldBe 1
      } finally {
        Files.deleteIfExists(outPath)
      }
    }

    "create the parent directory of the output path when writing the proto" in {
      val tmpRoot = Files.createTempDirectory("intermediate-dar-parentdir-")
      val nested  = tmpRoot.resolve("a").resolve("b").resolve("c").resolve("out.binpb")
      try {
        Decode.run(Decode.ParsedArgs(FixtureDar, nested)) shouldBe Right(())
        Files.exists(nested.getParent) shouldBe true
        Files.size(nested) should be > 0L
      } finally {
        if (Files.exists(tmpRoot)) {
          val stream = Files.walk(tmpRoot)
          try stream.sorted(java.util.Comparator.reverseOrder()).forEach(p => Files.deleteIfExists(p))
          finally stream.close()
        }
      }
    }

    "report a Left when the output path cannot be opened for writing" in {
      val tmpDir = Files.createTempDirectory("intermediate-dar-asdir-")
      try {
        Decode.run(Decode.ParsedArgs(FixtureDar, tmpDir)) match {
          case Left(message) => (message: String) should include(tmpDir.toString)
          case Right(())     => fail("expected Left when output path is a directory, not a file")
        }
      } finally {
        Files.deleteIfExists(tmpDir)
      }
    }
  }

  private def stripPartyAnalyses(dar: IntermediateDar): IntermediateDar = {
    def stripPkg(pkg: IntermediatePackage): IntermediatePackage =
      pkg.copy(modules = pkg.modules.map { m =>
        m.copy(templates = m.templates.map { t =>
          t.copy(
            signatories = None,
            observers = None,
            choices = t.choices.map(c => c.copy(controllers = None, observers = None)),
          )
        })
      })
    dar.copy(
      main = dar.main.map(stripPkg),
      dependencies = dar.dependencies.map(stripPkg),
    )
  }
}
