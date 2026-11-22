using System.Collections.Generic;
namespace KAONOMI.Core.Models;

public class YoutubeVideo : EntityBase, ISoftDeletable, ITagged, IVersioned
{

    public string videoId { get; set; }
    public string description { get; set; }
    public string url { get; set; }
    public bool isDeleted { get; set; } = false;

    /// <summary>
    /// The entities of the video (people, places, organisations, things…), "entities" in JSON: those the NER of the
    /// server finds in its title, description and comment at each save, and those added by hand (<see cref="manualEntities"/>).
    /// </summary>
    public List<string> entities { get; set; } = new List<string>();

    /// <summary>The entities added by hand: kept at each save, whatever the NER finds; always in <see cref="entities"/> too.</summary>
    public List<string> manualEntities { get; set; } = new List<string>();
}