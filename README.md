# EphysVisualization Bonsai Library

Visualization tools for electrophysiology data in [Bonsai](https://bonsai-rx.org/).

### Development
- `dotnet build` and `dotnet test` from the repository root. The tests target
  .NET Framework x64, so they run on Windows only.
- `.bonsai/Setup.ps1` installs a local Bonsai for running the library.
- The `docs/bonsai-docfx` submodule is needed only to build the docs; see
  [docs/README.md](docs/README.md).

### Upstream 
Portions of this library grew out of the
[`WaveformVisualizer`](https://github.com/bonsai-rx/ephys/blob/main/src/Bonsai.Ephys.Design/WaveformVisualizer.cs)
in `Bonsai.Ephys`.
