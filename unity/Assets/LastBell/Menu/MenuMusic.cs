using UnityEngine;

namespace LastBell.Menu
{
    public sealed class MenuMusic : MonoBehaviour
    {
        const string VolumeKey = "LastBell.Menu.MusicVolume";
        [Range(0, 1)] public float DefaultVolume = 0.6f;
        public AudioSource Source { get; private set; }
        public float Volume => PlayerPrefs.GetFloat(VolumeKey, DefaultVolume);
        float envelope;
        bool fadingOut;
        void Awake()
        {
            Source = gameObject.AddComponent<AudioSource>();
            Source.clip = Resources.Load<AudioClip>("Menu/MainMenuMusic");
            Source.loop = true; Source.playOnAwake = false; Source.spatialBlend = 0; Source.dopplerLevel = 0;
            Source.volume = 0;
            if (Source.clip) Source.Play();
        }
        void Update()
        {
            envelope = Mathf.MoveTowards(envelope, fadingOut ? 0 : 1, Time.unscaledDeltaTime / (fadingOut ? 0.35f : 0.8f));
            Source.volume = Volume * envelope;
        }
        public void SetVolume(float value)
        {
            PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value));
            if (Source) Source.volume = Volume * envelope;
        }
        public void FadeOut() { fadingOut = true; }
        void OnDestroy() { if (Source) Source.Stop(); PlayerPrefs.Save(); }
    }
}
