using System;
using System.IO;
using UnityEditor.Android;

namespace S014.Quest.Editor
{
    /// <summary>
    /// Unity OpenXR and Meta OVRPlugin both provide the Khronos OpenXR loader.
    /// Keep the loader from the first dependency and have Gradle package just one
    /// ARM64 copy in the application APK.
    /// </summary>
    internal sealed class S014QuestNativeLibraryPackagingPostprocessor : IPostGenerateGradleAndroidProject
    {
        private const string PackagingBlock = "    packaging {\n        jniLibs {\n            pickFirsts += ['lib/arm64-v8a/libopenxr_loader.so']\n        }\n    }\n\n";
        private const string BuildTypesMarker = "    buildTypes {";

        public int callbackOrder => 1000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var gradleRoot = Directory.GetParent(Path.GetFullPath(path));
            if (gradleRoot == null) return;
            var launcherGradle = Path.Combine(gradleRoot.FullName, "launcher", "build.gradle");
            if (!File.Exists(launcherGradle)) return;

            var source = File.ReadAllText(launcherGradle);
            if (source.IndexOf("pickFirsts += ['lib/arm64-v8a/libopenxr_loader.so']", StringComparison.Ordinal) >= 0)
                return;

            var markerIndex = source.IndexOf(BuildTypesMarker, StringComparison.Ordinal);
            if (markerIndex < 0)
                throw new InvalidOperationException("Cannot add the Quest OpenXR loader packaging rule: launcher build.gradle has no buildTypes block.");

            source = source.Insert(markerIndex, PackagingBlock);
            File.WriteAllText(launcherGradle, source);
        }
    }
}
