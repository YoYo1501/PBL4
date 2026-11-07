plugins {
    `java-library`
}

dependencies {
    implementation(project(":modules:control-transport"))
    implementation(project(":modules:control-protocol"))
    implementation(project(":modules:network-core"))
}

java {
    sourceCompatibility = JavaVersion.VERSION_21
    targetCompatibility = JavaVersion.VERSION_21
}

tasks.withType<JavaCompile>().configureEach { options.release = 21; options.encoding = "UTF-8" }
