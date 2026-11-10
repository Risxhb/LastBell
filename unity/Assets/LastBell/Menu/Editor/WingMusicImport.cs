using UnityEditor;
using UnityEngine;

namespace LastBell.Menu.EditorTools
{
    // Wing songs (Resources/Menu/WingMusic/<area>/) stream from disk as Vorbis: they are long and need no tight sync.
    // Applies to new songs dropped into those folders, so they need no manual Inspector change.
    sealed class WingMusicImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/" + MenuMusic.WingFolder)) return;
            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            importer.defaultSampleSettings = settings;
        }
    }
}
