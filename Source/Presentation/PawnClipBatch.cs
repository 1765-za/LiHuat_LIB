using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace LihuatLib.Presentation
{
    public sealed class PawnClipBatch : IDisposable
    {
        private readonly LihuatLib.Presentation.IndexedInstanceDraw instanceDraw = new LihuatLib.Presentation.IndexedInstanceDraw();
        [StructLayout(LayoutKind.Sequential)]
        private struct Instance
        {
            internal Vector4 Position,Shape,Tint,Motion;
            internal bool Matches(in Instance other)=>Position.Equals(other.Position)&&Shape.Equals(other.Shape)
                &&Tint.Equals(other.Tint)&&Motion.Equals(other.Motion);
        }
        public const int InstanceBytes=64;
        public const int SectorCount=12;
        private static readonly int instancesId=Shader.PropertyToID("_Clips"),timeId=Shader.PropertyToID("_ClipTime");
        private readonly Material material;
        private readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        private Instance[] instances=new Instance[16];
        private bool[] dirty=new bool[16];
        private ComputeBuffer buffer;
        private Bounds bounds;
        private int firstDirty,lastDirty,uploadedCount;
        private bool disposed;
        public int Count {get;private set;}
        public int UploadedBytes {get;private set;}
        public PawnClipKind Kind {get;}
        public static bool Supported=>SystemInfo.supportsInstancing&&SystemInfo.supports2DArrayTextures;

        public PawnClipBatch(PawnClipKind kind)
        {
            if(!Enum.IsDefined(typeof(PawnClipKind),kind))throw new ArgumentOutOfRangeException(nameof(kind));
            Kind=kind;
            material=new Material(Verse.DefDatabase<Verse.ShaderDef>.GetNamed("Lihuat_PawnClip").Shader){name="LihuatPawnClip",enableInstancing=true,hideFlags=HideFlags.HideAndDontSave};
            material.SetTexture("_Frames",PawnClipAssets.Frames(kind.ToString()));
            BeginFrame();
        }
        public void BeginFrame()
        {
            ThrowIfDisposed();Count=UploadedBytes=0;firstDirty=int.MaxValue;lastDirty=-1;
        }
        public void Add(Vector3 center,Vector2 size,Color tint,float opacity=1f,float speed=1f,float phase=0f,
            float intensity=1f,float rotationDegrees=0f,PawnBurstHalf half=PawnBurstHalf.Whole)
            => AddClip(center,size,tint,opacity,speed,phase,intensity,rotationDegrees,half,0);

        public void AddSector(Vector3 center,Vector2 size,Color tint,int sector,float opacity=1f,float speed=1f,float phase=0f,
            float intensity=1f,float rotationDegrees=0f,PawnBurstHalf half=PawnBurstHalf.Whole)
        {
            if(sector<0||sector>=SectorCount)throw new ArgumentOutOfRangeException(nameof(sector));
            AddClip(center,size,tint,opacity,speed,phase,intensity,rotationDegrees,half,sector+1);
        }

        private void AddClip(Vector3 center,Vector2 size,Color tint,float opacity,float speed,float phase,
            float intensity,float rotationDegrees,PawnBurstHalf half,int sector)
        {
            ThrowIfDisposed();
            if(opacity<=.005f||tint.a<=.005f||intensity<=0||size.x<=0||size.y<=0)return;
            if(Count==instances.Length){Array.Resize(ref instances,instances.Length*2);Array.Resize(ref dirty,instances.Length);}
            var item=new Instance{Position=new Vector4(center.x,center.y,center.z,sector),
                Shape=new Vector4(size.x,size.y,rotationDegrees*Mathf.Deg2Rad,Mathf.Clamp01(opacity)),Tint=tint,
                Motion=new Vector4(speed,phase,intensity,(float)half)};
            if(!instances[Count].Matches(in item)){instances[Count]=item;dirty[Count]=true;}
            if(dirty[Count]){firstDirty=Math.Min(firstDirty,Count);lastDirty=Count;}
            float extent=size.magnitude;
            var itemBounds=new Bounds(center,new Vector3(extent,.1f,extent));
            if(Count==0)bounds=itemBounds;else bounds.Encapsulate(itemBounds);
            Count++;
        }
        public void AddAround(Vector3 center,Vector2 size,Color tint,float backAltitude,float frontAltitude,
            float opacity=1f,float speed=1f,float phase=0f,float intensity=1f)
        {
            Add(new Vector3(center.x,backAltitude,center.z),size,tint,opacity,speed,phase,intensity,half:PawnBurstHalf.Back);
            Add(new Vector3(center.x,frontAltitude,center.z),size,tint,opacity,speed,phase,intensity,half:PawnBurstHalf.Front);
        }
        public bool Draw(float timeSeconds,Camera camera)
        {
            ThrowIfDisposed();UploadedBytes=0;
            if(!Supported||!material.shader.isSupported||Count==0||camera==null)return false;
            if(buffer==null||buffer.count<Count)
            {
                buffer?.Release();buffer=new ComputeBuffer(instances.Length,InstanceBytes,ComputeBufferType.Structured);
                properties.SetBuffer(instancesId,buffer);firstDirty=0;lastDirty=Count-1;
            }
            if(lastDirty>=firstDirty || Count>uploadedCount)
            {
                int uploaded=Count;buffer.SetData(instances,0,0,uploaded);uploadedCount=uploaded;
                Array.Clear(dirty,0,uploaded);UploadedBytes=uploaded*InstanceBytes;firstDirty=int.MaxValue;lastDirty=-1;
            }
            properties.SetFloat(timeId,timeSeconds);
            instanceDraw.Draw(FlowAuraAssets.Quad, material, bounds, Count, properties, camera);
            return true;
        }
        private void ThrowIfDisposed(){if(disposed)throw new ObjectDisposedException(nameof(PawnClipBatch));}
        public void Dispose()
        {
            if(disposed)return;disposed=true;buffer?.Release();buffer=null;instanceDraw.Dispose();UnityEngine.Object.Destroy(material);
        }
    }
}
