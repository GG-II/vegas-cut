// API falsa de VEGAS (solo lo que usan los scripts) para compilar y probar sin Vegas.
using System; using System.Collections.Generic;
namespace ScriptPortal.Vegas {
public class Timecode {
  public double ms; public Timecode(double m){ms=m;}
  public double ToMilliseconds(){return ms;}
  public static Timecode FromMilliseconds(double m){return new Timecode(m);}
  public static Timecode operator +(Timecode a, Timecode b){return new Timecode(a.ms+b.ms);}
  public override string ToString(){return (ms/1000).ToString("0.000");}
}
public class PlugInNode { public string Name, UniqueID; public List<PlugInNode> Hijos=new List<PlugInNode>();
  public PlugInNode GetChildByUniqueID(string id){ foreach(var h in Hijos) if(h.UniqueID==id) return h; return null; } }
public class OFXParameter { public string Name; }
public class OFXStringParameter: OFXParameter { public string Value { get; set; } }
public class OFXDoubleParameter: OFXParameter { public double Value { get; set; } }
public class OFXEffect { public List<OFXParameter> Parameters=new List<OFXParameter>(); public int Cambios;
  public OFXParameter FindParameterByName(string n){ foreach(var p in Parameters) if(p.Name==n) return p; return null; }
  public void AllParametersChanged(){Cambios++;} }
public class Effect { public PlugInNode PlugIn; public bool Bypass; public bool IsOFX=true; public OFXEffect OFXEffect=new OFXEffect();
  public Effect(PlugInNode p){ PlugIn=p; if (p!=null && p.UniqueID!=null && p.UniqueID.Contains("titlesandtext")) {
    OFXEffect.Parameters.Add(new OFXStringParameter{Name="Text",Value=""}); OFXEffect.Parameters.Add(new OFXDoubleParameter{Name="Scale",Value=1}); }
    if (p!=null && p.UniqueID=="sombra") OFXEffect.Parameters.Add(new OFXDoubleParameter{Name="Blur",Value=0}); } }
public enum MediaType { Unknown, Video, Audio }
public class MediaStream { public Media Parent; public MediaType MediaType; }
public class MediaStreams : List<MediaStream> { public MediaStream GetItemByMediaType(MediaType t, int i){ foreach(var m in this) if(m.MediaType==t && i--==0) return m; return null; } }
public class Media { public string FilePath; public bool Generada; public Effect Generator; public MediaStreams Streams=new MediaStreams();
  public bool IsGenerated(){return Generada;}
  public Timecode Length=new Timecode(0);
  public Media(){}
  public Media(string ruta){ FilePath=ruta; if (ruta.EndsWith(".mp4")) Streams.Add(new MediaStream{Parent=this,MediaType=MediaType.Video}); Streams.Add(new MediaStream{Parent=this,MediaType=MediaType.Audio}); if (ruta.EndsWith(".mp4")) { Streams.Add(new MediaStream{Parent=this,MediaType=MediaType.Audio}); Streams.Add(new MediaStream{Parent=this,MediaType=MediaType.Audio}); } Length=new Timecode(LargoFalso); }
  public static double LargoFalso=5000;
  public Media(PlugInNode p){ Generada=true; Generator=new Effect(p); Streams.Add(new MediaStream{Parent=this,MediaType=MediaType.Video}); } }
public class Fade { public Timecode Length = new Timecode(0); }
public class Take { public Media Media; public Timecode Offset = new Timecode(0); }
public class Effects : List<Effect> {}
public class TrackEvent {
  public Timecode Start, Length; public Fade FadeIn = new Fade(), FadeOut = new Fade(); public bool Mute, Selected; public TrackEventGroup Group; public bool IsGrouped { get { return Group != null; } } public double PlaybackRate = 1; public Take ActiveTake; public Track Track; public double Offset;
  public Timecode End { get { return new Timecode(Start.ms+Length.ms);} }
  public TrackEvent Split(Timecode off){
    var e=(TrackEvent)MemberwiseClone(); e.FadeIn=new Fade(); e.FadeOut=FadeOut; FadeOut=new Fade(); e.Start=new Timecode(Start.ms+off.ms); e.Length=new Timecode(Length.ms-off.ms); e.Offset=Offset+off.ms; if (ActiveTake!=null) { e.ActiveTake=new Take(); e.ActiveTake.Media=ActiveTake.Media; e.ActiveTake.Offset=new Timecode(ActiveTake.Offset.ms+off.ms*PlaybackRate); }
    Length=new Timecode(off.ms); Track.Events.Add(e); if (Group!=null) { e.Group=null; Group.Add(e); } return e; }
  public Take AddTake(MediaStream m){ ActiveTake=new Take{Media=m.Parent}; return ActiveTake; }
}
public class AudioEvent: TrackEvent {} public class VideoEvent: TrackEvent { public Effects Effects=new Effects(); }
public class Events : List<TrackEvent> { public new void Remove(TrackEvent e){ if (e.Group!=null) e.Group.Remove(e); base.Remove(e);} }
public class TrackEventGroup : List<TrackEvent> {
  public new void Add(TrackEvent e){ if (e.Group!=null) throw new Exception("ya esta en un grupo"); e.Group=this; base.Add(e); }
  public new void Remove(TrackEvent e){ if (base.Remove(e)) e.Group=null; } }
public enum EnvelopeType { Volume, Pan }
public class EnvelopePoint { public Timecode X; public double Y; public EnvelopePoint(Timecode x, double y){X=x;Y=y;} }
public class EnvelopePoints : List<EnvelopePoint> { public new void Add(EnvelopePoint p){ foreach(var q in this) if(Math.Abs(q.X.ms-p.X.ms)<0.01) throw new Exception("punto repetido"); base.Add(p); Sort((a,b)=>a.X.ms.CompareTo(b.X.ms)); }
  public new void Remove(EnvelopePoint p){ if (IndexOf(p)==0) throw new Exception("el primer punto no se borra"); base.Remove(p);} }
public class Envelope { public EnvelopeType Type; public EnvelopePoints Points=new EnvelopePoints(); public Envelope(EnvelopeType t){Type=t;} }
public class Envelopes : List<Envelope> { public Track Pista; public new void Add(Envelope e){ e.Points.Add(new EnvelopePoint(new Timecode(0),1)); base.Add(e);} public Envelope FindByType(EnvelopeType t){ foreach(var e in this) if(e.Type==t) return e; return null; } }
public class Track { public int Index; public string Name; public bool Mute; public Events Events=new Events(); public Envelopes Envelopes=new Envelopes(); public virtual bool IsAudio(){return false;}
  public Track(){} public Track(int i, string n){Index=i;Name=n;} }
public class AudioTrack: Track { public override bool IsAudio(){return true;} public AudioTrack(){} public AudioTrack(int i,string n):base(i,n){} public float Volume=1;
  public AudioEvent AddAudioEvent(Timecode s, Timecode l){ var e=new AudioEvent{Start=s,Length=l,Track=this}; Events.Add(e); return e; } }
public class VideoTrack: Track { public VideoTrack(){} public VideoTrack(int i,string n):base(i,n){}
  public VideoEvent AddVideoEvent(Timecode s, Timecode l){ var e=new VideoEvent{Start=s,Length=l,Track=this}; Events.Add(e); return e; } }
public class Marker { public Timecode Position; public string Label; public Marker(){} public Marker(Timecode p, string s){Position=p;Label=s;} }
public class Region: Marker { public Timecode Length; public Region(Timecode p, Timecode l, string s){Position=p;Length=l;Label=s;} }
public class VideoProps { public double FrameRate=59.94; }
public class MediaPool : List<Media> { public Media Find(string r){ foreach(var m in this) if(m.FilePath==r) return m; return null; } }
public class Project { public string FilePath; public MediaPool MediaPool=new MediaPool(); public List<TrackEventGroup> Groups=new List<TrackEventGroup>(); public List<Track> Tracks=new List<Track>(); public List<Marker> Markers=new List<Marker>(); public List<Region> Regions=new List<Region>(); public VideoProps Video=new VideoProps(); public Timecode Length=new Timecode(0);}
public class Transport { public Timecode SelectionStart=new Timecode(0), SelectionLength=new Timecode(0), CursorPosition=new Timecode(0); public int Reproducir; public void Play(){Reproducir++;} }
public enum RenderStatus { Complete, Canceled, Failed }
public class RenderTemplate { public string Name="PCM 16"; public bool IsValid(){return true;} }
public class Renderer { public string FileExtension="*.wav"; public List<RenderTemplate> Templates=new List<RenderTemplate>{new RenderTemplate()}; }
public class RenderArgs { public string OutputFile; public RenderTemplate RenderTemplate; public Timecode Start, Length; }
public class Vegas { public Project Project=new Project(); public PlugInNode Generators=new PlugInNode(); public Transport Transport=new Transport(); public List<Renderer> Renderers=new List<Renderer>{new Renderer()};
  public Func<RenderArgs,RenderStatus> OnRender; public RenderStatus Render(RenderArgs a){return OnRender(a);} }
public class UndoBlock : IDisposable { public UndoBlock(string s){} public void Dispose(){} }
}
