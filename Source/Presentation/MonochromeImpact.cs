using System;
using UnityEngine;

namespace LihuatLib.Presentation
{
    public sealed class MonochromeImpact : IDisposable
    {
        private readonly Material material;
        private static readonly int parametersId=Shader.PropertyToID("_Impact");
        private bool disposed;
        public const float DefaultDuration=.42f;
        public MonochromeImpact()
        {
            material=new Material(Verse.DefDatabase<Verse.ShaderDef>.GetNamed("Lihuat_MonochromeImpact").Shader){name="LihuatMonochromeImpact",hideFlags=HideFlags.HideAndDontSave};
        }
        // Input must be the world camera image before UI composition; the caller owns both targets.
        public bool Render(RenderTexture source,RenderTexture destination,float elapsedSeconds,
            float duration=DefaultDuration,float strength=1f,float threshold=.5f,float hardness=.42f,bool negative=true)
        {
            if(disposed)throw new ObjectDisposedException(nameof(MonochromeImpact));
            if(source==null||destination==null)throw new ArgumentNullException(source==null?nameof(source):nameof(destination));
            if(source==destination)throw new ArgumentException("Screen effects require separate source and destination targets");
            bool active=elapsedSeconds>=0&&elapsedSeconds<duration&&duration>0&&strength>0;
            if(!active){Graphics.Blit(source,destination);return false;}
            float fade=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(duration*.35f,duration,elapsedSeconds));
            material.SetVector(parametersId,new Vector4(Mathf.Clamp01(strength)*fade,negative?0:1,Mathf.Clamp01(threshold),Mathf.Clamp(hardness,0,.499f)));
            Graphics.Blit(source,destination,material);return true;
        }
        public void Dispose(){if(disposed)return;disposed=true;UnityEngine.Object.Destroy(material);}
    }
}
