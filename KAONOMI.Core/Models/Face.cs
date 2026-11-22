using System.Collections.Generic;

namespace KAONOMI.Core.Models
{
    /// <summary>
    /// A face enrolled under a name by /vision of the chat (KAONOMI.VisionOpenCV): one document per picture kept
    /// (index <c>face</c>), its id given by the chat from the name and the file. It holds the picture itself (the face
    /// aligned, 112 x 112, a JPEG in base 64) and what the recognition model says of it (128 numbers), so that the
    /// faces enrolled on a machine are found again on another one, or after its folder was lost. Really erased when
    /// the name is forgotten: nothing of a face is kept then.
    /// </summary>
    public class Face : EntityBase
    {
        /// <summary>The name the face was enrolled under.</summary>
        public string name { get; set; } = string.Empty;

        /// <summary>The name of the picture in the folder of the name (<c>~/kaonomi/vision/faces/&lt;name&gt;</c>).</summary>
        public string file { get; set; } = string.Empty;

        /// <summary>The picture: the face aligned (112 x 112), a JPEG in base 64.</summary>
        public string picture { get; set; } = string.Empty;

        /// <summary>What the recognition model says of the face: two faces are compared by the cosine of these numbers.</summary>
        public List<double> feature { get; set; } = new List<double>();

        /// <summary>The recognition model the feature comes from (e.g. <c>face_recognition_sface_2021dec</c>).</summary>
        public string model { get; set; } = string.Empty;
    }
}
