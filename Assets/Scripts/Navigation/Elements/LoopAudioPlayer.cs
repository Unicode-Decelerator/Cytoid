using System;
using System.Linq.Expressions;
using DG.Tweening;
using E7.Introloop;
using UniRx.Async;
using UnityEngine;
using UnityEngine.Audio;

public class LoopAudioPlayer : SingletonMonoBehavior<LoopAudioPlayer>, ScreenChangeListener
{
    private const bool PrintDebugMessages = true;

    public IntroloopAudio defaultLoopAudio;
    public IntroloopAudio resultLoopAudio;
    public AudioMixerGroup audioMixerGroup;
    
    private float maxVolume = 1f;
    private IntroloopAudio mainLoopAudio;
    private IntroloopAudio playingAudio;
    private bool isFadedOut;
    private DateTime asyncToken;

    protected override void Awake()
    {
        base.Awake();
        
        if (GameObject.FindGameObjectsWithTag("LoopAudioPlayer").Length > 1)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
        IntroloopPlayer.Instance.SetMixerGroup(audioMixerGroup);
        Context.PostSceneChanged.AddListener(PostSceneChanged);
        Context.CharacterManager.OnActiveCharacterSet.AddListener(asset =>
        {
            SetMainAudio(asset.musicAudio != null ? asset.musicAudio : defaultLoopAudio);
        });
        
        Context.OnApplicationInitialized.AddListener(Initialize);
    }

    private void Initialize()
    {
        Context.ScreenManager.AddHandler(this);
        UpdateMaxVolume();
    }

    public void UpdateMaxVolume()
    {
        var previousMaxVolume = maxVolume;
        maxVolume = Context.Player.Settings.MusicVolume;
        if (maxVolume == 0) maxVolume = 0.000001f;
        audioMixerGroup.audioMixer.GetFloat("MasterVolume", out var currentMixerGroupVolume);
        if (PrintDebugMessages) print($"LoopAudioPlayer: Current mixer group volume is {currentMixerGroupVolume}");
        var currentVolume = ConvertTo01Volume(currentMixerGroupVolume);
        var currentVolumePercentage = Mathf.Clamp01(currentVolume / previousMaxVolume);
        var mixerGroupVolume = ConvertToMixerGroupVolume(currentVolumePercentage * maxVolume);
        audioMixerGroup.audioMixer.SetFloat("MasterVolume", mixerGroupVolume);
        if (PrintDebugMessages) print($"LoopAudioPlayer: Mixer group volume set to {mixerGroupVolume}");
    }

    private static float ConvertToMixerGroupVolume(float f)
    {
        if (f == 0) f = 0.001f;
        return Mathf.Log(f) * 20;
    }
    
    private static float ConvertTo01Volume(float f)
    {
        return Mathf.Exp(f / 20);
    }

    public void PlayAudio(IntroloopAudio audio, float fadeInDuration = 0.5f, float crossfadeDuration = 0.5f)
    {
        if (playingAudio == audio) return;
        var duration = playingAudio != null ? crossfadeDuration : fadeInDuration;
        if (PrintDebugMessages) print("LoopAudioPlayer: Played audio " + audio.name);
        playingAudio = audio;
        IntroloopPlayer.Instance.Play(audio, duration);
    }

    public void StopAudio(float fadeOutDuration = 0.5f)
    {
        if (playingAudio == null) return;
        if (PrintDebugMessages) print("LoopAudioPlayer: Stopped audio " + playingAudio.name);
        playingAudio = null;
        IntroloopPlayer.Instance.Stop(fadeOutDuration);
    }
    
    public void FadeOutLoopPlayer(float duration = 1f)
    {
        isFadedOut = true;
        audioMixerGroup.audioMixer.DOKill();
        if (duration == 0) audioMixerGroup.audioMixer.SetFloat("MasterVolume", -80f);
        else audioMixerGroup.audioMixer.DOSetFloat("MasterVolume", -80f, duration).SetEase(Ease.Linear);
    }

    public void FadeInLoopPlayer(float duration = 1f)
    {
        isFadedOut = false;
        audioMixerGroup.audioMixer.DOKill();
        if (duration == 0) audioMixerGroup.audioMixer.SetFloat("MasterVolume", ConvertToMixerGroupVolume(maxVolume));
        else audioMixerGroup.audioMixer.DOSetFloat("MasterVolume", ConvertToMixerGroupVolume(maxVolume), duration).SetEase(Ease.Linear);
    }

    public void SetMainAudio(IntroloopAudio audio)
    {
        mainLoopAudio = audio;
        if (playingAudio != null)
        {
            PlayAudio(mainLoopAudio, 1f);
        }
    }

    public async void OnScreenChangeStarted(Screen from, Screen to)
    {
        if (to is GamePreparationScreen || to is TierSelectionScreen)
        {
            FadeOutLoopPlayer();
            return;
        }
        if ((from is GamePreparationScreen || from is TierSelectionScreen) && to != null)
        {
            FadeInLoopPlayer();
            PlayAudio(mainLoopAudio);
            return;
        }
        if (from is InitializationScreen && to is MainMenuScreen)
        {
            PlayAudio(mainLoopAudio, 0);
            return;
        }
        if (to is ResultScreen || to is TierBreakScreen)
        {
            PlayAudio(resultLoopAudio, 0);
            await UniTask.DelayFrame(5); // Introloop bug: Audio not switched immediately, causing ear rape
            FadeInLoopPlayer(0);
        }
    }

    public void PostSceneChanged(string prev, string next)
    {
        UpdateMaxVolume();
        Context.ScreenManager.AddHandler(this);
    }

    public void OnScreenChangeFinished(Screen from, Screen to) => Expression.Empty();
}