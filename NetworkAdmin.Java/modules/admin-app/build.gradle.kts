plugins {
    application
}

dependencies {
    implementation(project(":modules:admin-runtime"))
    implementation(project(":modules:control-transport"))
    implementation(project(":modules:control-protocol"))
    implementation(project(":modules:screen-share-server"))
    implementation(project(":modules:screen-share-protocol"))
    testImplementation("org.junit.jupiter:junit-jupiter:5.11.4")
    testImplementation(project(":modules:client-app"))
    testImplementation(project(":modules:client-runtime"))
    testImplementation(project(":modules:network-core"))
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
}

application { mainClass.set("vn.networkadmin.admin.app.AdminApplication") }

java {
    sourceCompatibility = JavaVersion.VERSION_21
    targetCompatibility = JavaVersion.VERSION_21
}

tasks.withType<JavaCompile>().configureEach { options.release = 21; options.encoding = "UTF-8" }

tasks.test { useJUnitPlatform() }
