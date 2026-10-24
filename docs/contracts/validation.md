# U3 proposal validation

October 21, 2026. Files: `U3.Contracts.cs`, `Fixtures.cs`, `Fixtures.csproj`. The package-free project explicitly targets C# **7.3** and compiles the DTO/interface proposal and its contract helpers together. Build artifacts were kept outside the review checkout at `E:/Collab/tools/u3-fixtures/`.

Executed with installed .NET SDK 10.0.401, runtime/reference pack 10.0.12:

```powershell
dotnet build docs/contracts/Fixtures.csproj --no-restore --configuration Release --property:BaseOutputPath=E:/Collab/tools/u3-fixtures/bin/ --property:BaseIntermediateOutputPath=E:/Collab/tools/u3-fixtures/obj/
dotnet E:/Collab/tools/u3-fixtures/bin/Release/net10.0/Fixtures.dll
```

Initial dependency-free restore/build was performed by `dotnet run` with the same output properties. Result: build success, **0 warnings / 0 errors; 32 fixtures passed**. Fixtures cover finite/huge/zero input, duration rounding, hazard lock timing, published dash arithmetic, hit/event identity, distinct same-tick attacks without immunity, HP overkill/overheal/Brace/shield arithmetic, replay/stale-attempt rejection, DPS/frozen time and checkpoint capability/schema header rejection. They do not exercise an actual simulation, collision engine, restore implementation or network transport.

Also compiled the exact files with the pinned Unity installation's Mono/Roslyn compiler against its .NET Standard 2.1 reference assembly:

```powershell
& 'E:/Collab/.unity-tools/6000.6.6f1/Editor/Data/MonoBleedingEdge/bin/mono.exe' 'E:/Collab/.unity-tools/6000.6.6f1/Editor/Data/MonoBleedingEdge/lib/mono/4.5/csc.exe' /noconfig /nologo /langversion:7.3 /warnaserror /nostdlib /target:library /reference:E:/Collab/.unity-tools/6000.6.6f1/Editor/Data/NetStandard/ref/2.1.0/netstandard.dll /out:E:/Collab/tools/u3-fixtures/U3.UnityProfile.dll E:/Collab/foundation-review/docs/contracts/U3.Contracts.cs E:/Collab/foundation-review/docs/contracts/Fixtures.cs
```

Result: **exit 0, no diagnostics**. `/noconfig` is required to avoid the compiler's desktop default `System.Core` reference conflicting with the supplied `netstandard` reference; the initial attempt without it failed with duplicate HashSet and was corrected. This establishes C# 7.3/API profile compilation of these proposal files only. They were not imported into the Unity project, executed in Play Mode, bound to #42, or validated for IL2CPP/Internet/co-op. Full authority restore, geometric boundary tests and consumer adaptation remain the review gates listed in [the contract](u3-review.md#staged-adoption-and-review-gates).

`git diff --check` passed in the review checkout. No production, scene, animation, reference or local-experiment files were changed by this work.
