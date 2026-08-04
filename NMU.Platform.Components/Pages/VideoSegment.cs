namespace NMU.Platform.Components.Pages;

public class VideoSegmentModel
{
    public double Start { get; set; }
    public double End { get; set; }
}

public class VideoSegmentCollection : System.Collections.Generic.List<VideoSegmentModel>
{
}
