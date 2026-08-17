// Ground_BellRevealInject.hlsl
// Visibility-only fragment for bell-revealed platforms: the Quibli base color with alpha
// driven purely by the expanding sound-wave envelope (+ edit-mode preview floor).
// NO trail code here — goo marks render on a separate TrailGooOverlay renderer.
// Included by Ground_Quibli_BellReveal.shader after Quibli's LitForwardPass_DR.hlsl.
#ifndef GROUND_BELL_REVEAL_INJECT_INCLUDED
#define GROUND_BELL_REVEAL_INJECT_INCLUDED

// Globals pushed every frame by BellSoundWave. Array length is locked by Unity at first
// SetGlobalVectorArray — keep in sync with BellSoundWave.MaxWaves.
#define BELL_WAVE_COUNT 8
float4 _BellWaveData[BELL_WAVE_COUNT];   // xy = center world XY, z = age seconds (<0 = inactive), w = emitter group
// Shape travels per wave, not per frame: each bell keeps its own config, so two bells can hold
// and fade for different durations even while their waves overlap.
float4 _BellWaveShape[BELL_WAVE_COUNT];  // x = waveSpeed, y = frontWidth, z = holdSeconds, w = fadeSeconds
float4 _BellWaveShape2[BELL_WAVE_COUNT]; // x = maxRadius, y = age the hold clock runs from (<0 = per-pixel)
// Per-renderer, stamped by the bell sharing this platform's parent; 0 = no bell, never revealed.
float  _BellWaveGroup;
// Alpha floor for edit-mode authoring: BellRevealEditPreview holds it at the config's preview
// value outside play mode and 0 otherwise. Unset in builds = 0 = no effect.
float  _BellWaveEditPreviewAlpha;

// One 8-bit alpha step: below it the platform contributes nothing, so discarding keeps a
// hidden platform out of the depth buffer that the ForwardLit pass writes.
#define BELL_REVEAL_MIN_ALPHA (1.0 / 255.0)

// Per-pixel reveal: the wavefront reaches this pixel at t = dist/speed; visibility rises
// over frontWidth, holds, then fades — so platforms appear and vanish in passing order.
// Only waves from the bell under the same parent count; every other bell leaves it hidden.
float BellWaveReveal(float2 posXY)
{
    float reveal = 0.0;
    [unroll]
    for (int i = 0; i < BELL_WAVE_COUNT; i++)
    {
        float speed    = max(_BellWaveShape[i].x, 1e-4);
        float riseTime = _BellWaveShape[i].y / speed;
        float age = _BellWaveData[i].z;
        float d   = distance(posXY, _BellWaveData[i].xy);
        float t   = age - d / speed;
        float rise = saturate(t / max(riseTime, 1e-4));
        // Releasing restarts the fade clock from one shared moment, but each pixel still lags it by
        // its own d/speed, so the vanish sweeps outward as a wave instead of switching off at once.
        float release = _BellWaveShape2[i].y;
        float fallT = t - (release < 0.0 ? riseTime : release);
        float fall = 1.0 - saturate((fallT - _BellWaveShape[i].z) / max(_BellWaveShape[i].w, 1e-4));
        float grp = _BellWaveData[i].w;
        float mine = abs(grp - _BellWaveGroup) < 0.5 ? 1.0 : 0.0;
        float active = (age >= 0.0 && d <= _BellWaveShape2[i].x) ? 1.0 : 0.0;
        reveal = max(reveal, min(rise, fall) * active * mine);
    }
    return reveal;
}

half4 BellRevealFragment(Varyings input) : SV_Target
{
    half4 color = StylizedPassFragment(input);
    float reveal = BellWaveReveal(input.positionWS.xy);
    color.a = max((half)reveal, (half)_BellWaveEditPreviewAlpha);
    clip(color.a - BELL_REVEAL_MIN_ALPHA);
    return color;
}
#endif
