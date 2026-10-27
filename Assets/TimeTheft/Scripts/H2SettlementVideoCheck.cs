#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

namespace TimeTheft.Editor
{
    // 생성/참조 MP4를 실제 1배속으로 디코딩하고 지정한 프레임을 저장한다. 씬에는 저장하지 않는다.
    public sealed class H2SettlementVideoCheck : MonoBehaviour
    {
        public string Status { get; private set; }="Idle";
        private bool running;
        public void Check(string path,string directory,int[] frames)
        { if(!running){running=true;StartCoroutine(Decode(path,directory,frames));} }
        private IEnumerator Decode(string path,string directory,int[] frames)
        {
            Directory.CreateDirectory(directory); var video=gameObject.AddComponent<VideoPlayer>();
            video.playOnAwake=false;video.source=VideoSource.Url;video.url=path;video.renderMode=VideoRenderMode.RenderTexture;
            video.sendFrameReadyEvents=true;video.isLooping=false;video.playbackSpeed=1;
            var rt=new RenderTexture(1280,720,0,RenderTextureFormat.ARGB32);rt.Create();video.targetTexture=rt;
            bool ended=false; string error=null;long last=-1;int next=0;
            video.errorReceived+=(v,e)=>{error=e;}; video.loopPointReached+=v=>{ended=true;};
            video.frameReady+=(v,frame)=>
            {
                last=frame;
                if(next>=frames.Length||frame<frames[next])return;
                var prior=RenderTexture.active;RenderTexture.active=rt;
                var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();
                File.WriteAllBytes(Path.Combine(directory,"Frame-"+frames[next]+".png"),tex.EncodeToPNG());
                Destroy(tex);RenderTexture.active=prior;next++;
            };
            try
            {
                Status="Preparing";video.Prepare();float deadline=Time.realtimeSinceStartup+25;
                while(!video.isPrepared&&error==null&&Time.realtimeSinceStartup<deadline)yield return null;
                if(!video.isPrepared)throw new InvalidOperationException(error??"Video prepare timed out");
                File.WriteAllText(Path.Combine(directory,"metadata.json"),"{\"fps\":"+video.frameRate+",\"frameCount\":"+video.frameCount+",\"length\":"+video.length.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
                Status="Playing at 1x";video.Play();deadline=Time.realtimeSinceStartup+(float)video.length+30;
                while(!ended&&error==null&&Time.realtimeSinceStartup<deadline)yield return null;
                if(error!=null||!ended)throw new InvalidOperationException(error??"Video playback timed out");
                File.WriteAllText(Path.Combine(directory,"decoded.json"),"{\"lastFrame\":"+last+",\"captures\":"+next+",\"time\":"+video.time.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
                Status="Decoded complete";
            }
            finally {video.Stop();video.targetTexture=null;Destroy(video);if(RenderTexture.active==rt)RenderTexture.active=null;rt.Release();Destroy(rt);running=false;}
        }
    }
}
#endif
