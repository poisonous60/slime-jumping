#!/usr/bin/env node
// driver.mjs — drive the running Unity Editor for the instrument -> read-log -> fix loop.
//
// Thin wrapper over the `unity-cli` tool (must be on PATH). Every subcommand maps
// to a `unity-cli` invocation that was verified by hand while authoring this skill.
// The Unity Editor must already be OPEN on this project; this script talks to it
// over unity-cli's local HTTP bridge — it does not launch Unity.
//
// Run with no args for usage.
import { spawnSync } from 'node:child_process';

const UNITY = 'unity-cli';

// Call unity-cli, return stdout. stderr is forwarded (unity-cli prints status there).
function cli(args, { input } = {}) {
  const r = spawnSync(UNITY, args, { input, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  if (r.error) {
    console.error(`unity-cli not runnable on PATH: ${r.error.message}`);
    process.exit(127);
  }
  if (r.stderr && r.stderr.trim()) process.stderr.write(r.stderr);
  return r.stdout || '';
}

// Run C# inside the editor by piping via stdin (avoids shell escaping).
const exec = (code) => cli(['exec'], { input: code });

// Resolve the live SlimeController (works in edit OR play mode).
const SLIME = 'var slime = UnityEngine.Object.FindFirstObjectByType<SlimeJumping.Controller.SlimeController>();';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// unity-cli `console` returns a JSON array of strings; each entry is
// "<message>\n<stacktrace...>". Keep first line only, optionally tag-filtered.
function parseConsole(out, tag) {
  const start = out.indexOf('[');
  let entries;
  try { entries = JSON.parse(start >= 0 ? out.slice(start) : out); }
  catch { entries = out.split('\n'); }
  return entries
    .map((e) => String(e).split('\n')[0])
    .filter((first) => !tag || first.includes(tag));
}

const [cmd, ...rest] = process.argv.slice(2);

switch (cmd) {
  case 'compile': {
    // Recompile is blocked in Play mode — refresh would no-op and we'd falsely
    // report "clean". Bail with a clear message instead.
    if (exec('return UnityEditor.EditorApplication.isPlaying;').includes('True')) {
      console.error('cannot compile: Unity is in Play mode. Exit Play, then re-run.');
      process.exit(2);
    }
    // refresh_unity triggers an async asset import + script recompile.
    cli(['refresh_unity']);
    await sleep(5000); // let the compiler finish
    const out = cli(['console', '--type', 'error,warning', '--lines', '60']);
    const errs = parseConsole(out).filter((l) => /CS\d{4}|error/i.test(l));
    if (errs.length) {
      console.log('COMPILE ERRORS:');
      for (const e of errs) console.log('  ' + e);
      process.exit(1);
    }
    console.log('compile clean (no errors/warnings)');
    break;
  }

  case 'attach': {
    const type = rest[0];
    if (!type) { console.error('usage: attach <ComponentFullName>'); process.exit(2); }
    // Find the SlimeController GO, add the component if absent, save the scene.
    // Idempotent — re-running is a no-op beyond a scene save.
    const code = `${SLIME}
if (slime == null) return "NO SLIME in active scene";
var go = slime.gameObject;
var t = System.Type.GetType("${type}, Assembly-CSharp");
if (t == null) return "TYPE NOT FOUND (compile first?): ${type}";
var c = go.GetComponent(t);
if (c == null) c = go.AddComponent(t);
UnityEditor.EditorUtility.SetDirty(go);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(go.scene);
return "attached " + t.Name + " to " + go.name + " (" + go.scene.name + " saved)";`;
    console.log(exec(code).trim());
    break;
  }

  case 'logs': {
    const tag = rest[0] || '';
    const lines = rest[1] || '80';
    // Debug.Log lines are type "log" — the default console reads only error+warning,
    // so request all three explicitly or the diagnostic output is invisible.
    const out = cli(['console', '--type', 'log,warning,error', '--lines', lines]);
    for (const line of parseConsole(out, tag)) console.log(line);
    break;
  }

  case 'state': {
    // Snapshot the public SlimeController getters. Most informative during Play.
    const code = `if (!UnityEditor.EditorApplication.isPlaying) return "state: enter Play mode first (runtime getters are null in edit mode)";
${SLIME}
if (slime == null) return "NO SLIME";
var p = slime.Probe;
float slideVel = slime.TangentSpeed - slime.CrawlVelocity;
return string.Format("state={0} contacts={1}/{2} pos={3} |vel|={4:F3} slideVel={5:F4} crawlVel={6:F4} tan={7} nrm={8}",
  slime.CurrentState, p != null ? p.ActiveContactCount : 0, p != null ? p.ContactCount : 0,
  slime.Position, slime.Velocity.magnitude, slideVel, slime.CrawlVelocity,
  slime.SurfaceTangent, slime.SurfaceNormal);`;
    console.log(exec(code).trim());
    break;
  }

  case 'shot': {
    const view = rest[0] || 'scene'; // scene | game
    // unity-cli resolves a relative --output_path against the Unity project dir
    // (SlimeJumping_v2/), so this lands in SlimeJumping_v2/Screenshots/.
    const path = rest[1] || `Screenshots/_diagnose_${view}.png`;
    console.log(cli(['screenshot', '--view', view, '--output_path', path]).trim());
    break;
  }

  case 'clear': {
    cli(['console', '--clear']);
    console.log('console cleared');
    break;
  }

  default:
    console.log(`driver.mjs — unity-cli wrapper for the instrument -> log -> fix loop
(Unity Editor must already be open on this project.)

  compile                  refresh + recompile, report CS errors (exit 1 if any)
  attach <FullTypeName>    add a debug component to the SlimeController GameObject + save
  logs [tag] [lines]       read console (log+warning+error), 1 line/entry, tag-filtered
  state                    dump live SlimeController getters (run during Play)
  shot [scene|game] [out]  screenshot a view (default: scene -> Screenshots/)
  clear                    clear the Unity console

Examples:
  node driver.mjs compile
  node driver.mjs attach SlimeJumping.Debugging.CornerWedgeAccelDebugger
  node driver.mjs logs CornerWedge 120
  node driver.mjs state`);
}
