using UnityEngine;

namespace SasquatchInspector.Showcase
{
    internal class ShowcaseMediaTarget : ScriptableObject
    {
        public Mesh previewMesh;

#if SASQ_AUDIO
        public AudioClip sfx;
#endif

        public GameObject vfxPrefab;

        public ShowcaseStatBlock statBlock;

        public ShowcaseStatBlock newBlock;
    }
}
