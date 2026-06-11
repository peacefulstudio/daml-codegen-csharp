ThisBuild / scalaVersion := "2.13.16"
ThisBuild / organization := "studio.peaceful.daml.codegen"
ThisBuild / version      := "0.1.0-SNAPSHOT"

resolvers += "Daml" at "https://repo1.maven.org/maven2/"

lazy val damlLfArchiveVersion = "3.4.11"

lazy val jvmHelper = (project in file("."))
  .settings(
    name := "daml-codegen-jvm-helper",
    Compile / PB.protoSources := Seq((ThisBuild / baseDirectory).value / ".." / "proto"),
    Compile / PB.targets := Seq(
      scalapb.gen() -> (Compile / sourceManaged).value / "scalapb"
    ),
    libraryDependencies ++= Seq(
      "com.daml" %% "daml-lf-archive-reader" % damlLfArchiveVersion,
      "com.thesamet.scalapb" %% "scalapb-runtime" % scalapb.compiler.Version.scalapbVersion % "protobuf",
      "org.scalatest" %% "scalatest" % "3.2.19" % Test
    ),
    assembly / mainClass := Some("studio.peaceful.daml.codegen.helper.Decode"),
    assembly / assemblyJarName := "daml-codegen-jvm-helper.jar",
    assembly / assemblyMergeStrategy := {
      case PathList("META-INF", "MANIFEST.MF")              => MergeStrategy.discard
      case PathList("META-INF", "versions", _, "module-info.class") => MergeStrategy.discard
      case PathList("module-info.class")                    => MergeStrategy.discard
      case PathList("META-INF", "io.netty.versions.properties") => MergeStrategy.first
      case PathList("META-INF", xs @ _*) if xs.lastOption.exists(_.endsWith(".SF"))  => MergeStrategy.discard
      case PathList("META-INF", xs @ _*) if xs.lastOption.exists(_.endsWith(".DSA")) => MergeStrategy.discard
      case PathList("META-INF", xs @ _*) if xs.lastOption.exists(_.endsWith(".RSA")) => MergeStrategy.discard
      case PathList("google", "protobuf", _*)               => MergeStrategy.first
      case PathList("scala", "annotation", "nowarn.class")  => MergeStrategy.first
      case PathList("scala", "annotation", "nowarn$.class") => MergeStrategy.first
      case x =>
        val old = (assembly / assemblyMergeStrategy).value
        old(x)
    },
    scalacOptions ++= Seq(
      "-deprecation",
      "-feature",
      "-Werror",
      "-Wunused:imports"
    ),
    coverageExcludedPackages := "studio\\.peaceful\\.daml\\.codegen\\.intermediate\\..*",
    coverageOutputCobertura := true,
    coverageOutputXML := true
  )
