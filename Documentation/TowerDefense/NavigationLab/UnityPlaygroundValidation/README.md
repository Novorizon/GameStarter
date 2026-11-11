# Unity Playground validation

Linux Unity 2022.3.62f1 / Mono / OpenGL software rendering. Result: **Passed**.

[smoke.json](smoke.json) records the executed stages; [smoke.png](smoke.png) is a real Camera.Render capture of the final E forest-single-medium run, with one attacking unit and no towers. The final stationary window was 200 logical ticks for all six small runs. This is a renderer/lifecycle smoke test, not a new full49 candidate score or Windows/device acceptance.

The explicit invocation exercised pause, exact10 microstep single-step, resume, camera toggles, user stop, fresh restart for A/B0/B1/C33/D12/E, A DynamicTerrain Unsupported before creation, exit/reenter Play, render capture and final cleanup. The .NET project verification also passed; its model tests do not replace Unity.

Reproduce with the installed Unity executable (do not pass -quit; the explicit smoke entry owns its exit):

```text
Unity -batchmode -projectPath <repository> -executeMethod NavigationLab.UnityPlayground.PlaygroundSmoke.Run --navigation-playground-report <absolute-report.json> -logFile <absolute-log.txt>
```

Normal menu usage never enables automatic application exit. DLL and input identity is checked by PlaygroundController against playground-bindings.json. Test plugins are explicitly referenced by the Editor-only assembly; Unity compiler response files for Game.HotUpdate and Game.Startup contained none of these plugin references.
