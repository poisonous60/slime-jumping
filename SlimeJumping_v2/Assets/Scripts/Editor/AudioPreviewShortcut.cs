using UnityEngine;
using UnityEditor;
using System.Reflection;

/// Editor-only clip audition over UnityEditor.AudioUtil. The API takes no volume or mixer group,
/// so a preview is the raw clip — not what the clip will sound like in game.
public static class AudioPreviewShortcut
{
    [MenuItem("Tools/Play Selected Audio _F1")]
    static void PlaySelectedAudio()
    {
        var clip = Selection.activeObject as AudioClip;
        if (clip == null)
        {
            Debug.Log("선택된 AudioClip이 없습니다.");
            return;
        }

        Play(clip);
    }

    public static void Play(AudioClip clip)
    {
        if (clip == null) return;

        StopAll();

        var audioUtilType = typeof(AudioImporter).Assembly
            .GetType("UnityEditor.AudioUtil");
        var method = audioUtilType.GetMethod(
            "PlayPreviewClip",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new System.Type[] { typeof(AudioClip), typeof(int), typeof(bool) },
            null
        );
        method.Invoke(null, new object[] { clip, 0, false });
    }

    [MenuItem("Tools/Stop All Audio _F3")]
    public static void StopAll()
    {
        var audioUtilType = typeof(AudioImporter).Assembly
            .GetType("UnityEditor.AudioUtil");
        var method = audioUtilType.GetMethod(
            "StopAllPreviewClips",
            BindingFlags.Static | BindingFlags.Public
        );

        if (method != null)
        {
            method.Invoke(null, null);
        }
        else
        {
            Debug.LogWarning("StopAllPreviewClips 메서드를 찾을 수 없습니다.");
        }
    }
}
