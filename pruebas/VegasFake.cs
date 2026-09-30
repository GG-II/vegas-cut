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
public class Media { public string FilePath; }
public class Take { public Media Media; }
public class TrackEvent {
  public Timecode Start, Length; public bool Mute; public Take ActiveTake; public Track Track; public double Offset;
  public Timecode End { get { return new Timecode(Start.ms+Length.ms);} }
  public TrackEvent Split(Timecode off){
    var e=(TrackEvent)MemberwiseClone(); e.Start=new Timecode(Start.ms+off.ms); e.Length=new Timecode(Length.ms-off.ms); e.Offset=Offset+off.ms;
    Length=new Timecode(off.ms); Track.Events.Add(e); return e; }
}
public class AudioEvent: TrackEvent {} public class VideoEvent: TrackEvent {}
public class Events : List<TrackEvent> { public new void Remove(TrackEvent e){ base.Remove(e);} }
public class Track { public int Index; public string Name; public bool Mute; public Events Events=new Events(); public virtual bool IsAudio(){return false;} }
public class AudioTrack: Track { public override bool IsAudio(){return true;} }
public class VideoTrack: Track {}
public class Marker { public Timecode Position; public string Label; public Marker(){} }
public class Region: Marker { public Timecode Length; public Region(Timecode p, Timecode l, string s){Position=p;Length=l;Label=s;} }
public class VideoProps { public double FrameRate=59.94; }
public class Project { public List<Track> Tracks=new List<Track>(); public List<Marker> Markers=new List<Marker>(); public List<Region> Regions=new List<Region>(); public VideoProps Video=new VideoProps(); public Timecode Length=new Timecode(0);}
public class Transport { public Timecode SelectionStart=new Timecode(0), SelectionLength=new Timecode(0);}
public enum RenderStatus { Complete, Canceled, Failed }
public class RenderTemplate { public string Name="PCM 16"; public bool IsValid(){return true;} }
public class Renderer { public string FileExtension="*.wav"; public List<RenderTemplate> Templates=new List<RenderTemplate>{new RenderTemplate()}; }
public class RenderArgs { public string OutputFile; public RenderTemplate RenderTemplate; public Timecode Start, Length; }
public class Vegas { public Project Project=new Project(); public Transport Transport=new Transport(); public List<Renderer> Renderers=new List<Renderer>{new Renderer()};
  public Func<RenderArgs,RenderStatus> OnRender; public RenderStatus Render(RenderArgs a){return OnRender(a);} }
public class UndoBlock : IDisposable { public UndoBlock(string s){} public void Dispose(){} }
}
