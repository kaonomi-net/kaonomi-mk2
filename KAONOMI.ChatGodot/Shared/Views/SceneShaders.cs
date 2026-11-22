using KAONOMI.Chat.Models;

namespace KAONOMI.Chat.Views;

// GLSL of the 3D scenes of the band, without any Avalonia type so that the Godot chat (KAONOMI.ChatGodot) links this
// file and translates the same shaders to the Godot shading language: the faces of KaonomiScene and the holographic
// display of JarvisScene look and move the same in both chats.

/// <summary>Faces of <see cref="KaonomiScene"/>: their images and the GLSL that shapes, opens and lights them.</summary>
public static class FaceShaders
{
    /// <summary>The face shown for <paramref name="animation"/> (MARI for any value that is not a face).</summary>
    public static FaceModel For(SceneAnimation animation) => animation switch
    {
        SceneAnimation.Hal => HalFace,
        SceneAnimation.MarieKilleuse => MarieKilleuseFace,
        SceneAnimation.Musuko => MusukoFace,
        SceneAnimation.Kaonami => KaonamiFace,
        SceneAnimation.Green => GreenFace,
        SceneAnimation.Circuit => CircuitFace,
        SceneAnimation.Motuko => MotukoFace,
        SceneAnimation.Kaonomi => KaonomiRobotFace,
        SceneAnimation.Rivet => RivetFace,
        SceneAnimation.Shiro => ShiroFace,
        SceneAnimation.Kingyo => KingyoFace,
        SceneAnimation.Hikari => HikariFace,
        SceneAnimation.Kamen => KamenFace,
        SceneAnimation.Aoi => AoiFace,
        SceneAnimation.Hagane => HaganeFace,
        SceneAnimation.Sora => SoraFace,
        SceneAnimation.Tama => TamaFace,
        SceneAnimation.Beni => BeniFace,
        _ => MariFace
    };

    /// <summary>
    /// A face of the scene: its image and the GLSL that shapes it. <see cref="Glsl"/> holds the style (<c>#define</c>)
    /// and the mouth constants; <see cref="Shape"/> defines <c>mouthLine(x)</c> (y of the closed mouth),
    /// <c>relief(p)</c> (height of the mesh) and <c>jaw(p)</c> (share of the jaw drop), in texture coordinates
    /// (x right, y down).
    /// </summary>
    public sealed record FaceModel(string Image, string Glsl, string Shape);

    // Kaonomi robot: face oval centred at (0.5, 0.5), eyes at y 0.445, nose at 0.545, smiling mouth (closed) along
    // y 0.615 at the centre and 0.6 at its corners (x 0.41 to 0.59), chin at 0.75, headphones on the sides (x 0.17 and
    // 0.83), hair from 0.12 to 0.88 across.
    public static readonly FaceModel MariFace = new("avares://KAONOMI.Chat/Assets/kaonomi-face.png", """
        #define HOLOGRAM
        const float MOUTH_X = 0.5;        // centre of the mouth
        const float MOUTH_HALF = 0.085;   // half width of the opening
        const float MOUTH_GAP = 0.012;    // band below the mouth line stretched into the opening
        const float JAW_DROP = 0.06;      // drop of the chin, mouth wide open
        const vec3 MOUTH_DARK = vec3(0.32, 0.06, 0.13);
        const vec3 MOUTH_LIGHT = vec3(0.85, 0.36, 0.45);   // the tongue

        """, """
        // The line of the closed mouth (a smile: its corners are higher).
        float mouthLine(float x)
        {
            float d = (x - 0.5) / 0.09;
            return 0.615 - 0.015 * d * d;
        }

        float relief(vec2 p)
        {
            float ax = abs(p.x - 0.5);
            float face = 0.34 * sqrt(oval(p, vec2(0.5, 0.5), vec2(0.27, 0.28)));
            face += 0.05 * bump(p, vec2(0.5, 0.545), vec2(0.035, 0.03));             // small nose
            face += 0.02 * bump(p, vec2(0.5, 0.5), vec2(0.025, 0.05));               // nose bridge
            face += 0.04 * bump(vec2(ax, p.y), vec2(0.17, 0.565), vec2(0.07, 0.05));  // cheeks
            face -= 0.025 * bump(vec2(ax, p.y), vec2(0.135, 0.445), vec2(0.07, 0.045)); // eye sockets
            face += 0.02 * bump(p, vec2(0.5, 0.71), vec2(0.08, 0.04));               // chin
            float hair = 0.24 * sqrt(oval(p, vec2(0.5, 0.4), vec2(0.4, 0.45)));
            float phones = 0.3 * bump(vec2(ax, p.y), vec2(0.33, 0.47), vec2(0.05, 0.12)); // headphones
            float neck = 0.08 * sqrt(oval(p, vec2(0.5, 0.82), vec2(0.12, 0.12)));
            return max(max(face, max(hair, phones)), neck);
        }

        // Jaw: everything below the line of the mouth, across the chin, drops with the opening of the mouth.
        float jaw(vec2 p)
        {
            float below = smoothstep(mouthLine(p.x), mouthLine(p.x) + MOUTH_GAP, p.y) * (1.0 - smoothstep(0.76, 0.84, p.y));
            float across = 1.0 - smoothstep(0.11, 0.2, abs(p.x - 0.5));
            return below * across;
        }

        """);

    // Eye of a computer: centre of the iris at (0.5, 0.478), glass dome of radius 0.27, red iris within 0.15, bezel
    // around 0.40, housing up to 0.48 (the pedestal below is cut off by the circle).
    public static readonly FaceModel HalFace = new("avares://KAONOMI.Chat/Assets/hal-eye.png", """
        #define HAL
        const vec2 EYE = vec2(0.5, 0.478);
        // No mouth: the eye glows with the voice instead (the mouth is out of the image and never opens).
        const float MOUTH_X = 0.5;
        const float MOUTH_HALF = 0.0;
        const float MOUTH_GAP = 1.0;
        const float JAW_DROP = 0.0;
        const vec3 MOUTH_DARK = vec3(0.0);
        const vec3 MOUTH_LIGHT = vec3(0.0);

        """, """
        float mouthLine(float x)
        {
            return -10.0;
        }

        float relief(vec2 p)
        {
            float r = length(p - EYE);
            float housing = 0.10 * sqrt(max(0.0, 1.0 - (r / 0.49) * (r / 0.49)));
            float b = (r - 0.40) / 0.04;
            float bezel = 0.06 * exp(-b * b);
            float lens = 0.22 * sqrt(max(0.0, 1.0 - (r / 0.27) * (r / 0.27)));   // glass dome
            return housing + bezel + lens;
        }

        float jaw(vec2 p)
        {
            return 0.0;
        }

        """);

    // Robot doll with a knife: face axis at x 0.5, spiral pupils at (0.368, 0.455) and (0.635, 0.455), nose at
    // (0.5, 0.545), grin whose upper and lower teeth meet along y 0.64 at the centre, 0.622 at its left corner (x 0.405)
    // and 0.596 at its right one (x 0.595),
    // robot chin plate down to 0.75, headphones at x 0.21 and 0.79, hearts of the chest at (0.5, 0.835) and
    // (0.5, 0.935), knife blade from (0.94, 0.33) to (0.81, 0.76), the hand on its handle around (0.8, 0.88).
    public static readonly FaceModel MarieKilleuseFace = new("avares://KAONOMI.Chat/Assets/mariekilleuse-face.png", """
        #define KILLER
        const vec2 EYE_LEFT = vec2(0.368, 0.455);
        const vec2 EYE_RIGHT = vec2(0.635, 0.455);
        const vec2 BLADE_TIP = vec2(0.94, 0.33);
        const vec2 BLADE_HEEL = vec2(0.81, 0.76);
        const float MOUTH_X = 0.5;
        const float MOUTH_HALF = 0.075;
        const float MOUTH_GAP = 0.01;
        const float JAW_DROP = 0.05;
        const vec3 MOUTH_DARK = vec3(0.25, 0.02, 0.06);
        const vec3 MOUTH_LIGHT = vec3(0.75, 0.15, 0.25);    // the tongue

        """, """
        // The line between the upper and the lower teeth of the grin (tilted: its right corner is higher), so that the
        // lower teeth go down with the jaw.
        float mouthLine(float x)
        {
            float t = clamp((x - 0.5) / 0.1, -1.0, 1.0);
            float t2 = t * t;
            return 0.6392 - 0.0106 * t + 0.005 * t2 - 0.042 * t2 * t2;
        }

        // Distance from p to the blade of the knife.
        float bladeDistance(vec2 p)
        {
            vec2 axis = BLADE_HEEL - BLADE_TIP;
            float t = clamp(dot(p - BLADE_TIP, axis) / dot(axis, axis), 0.0, 1.0);
            return length(p - BLADE_TIP - axis * t);
        }

        float relief(vec2 p)
        {
            float ax = abs(p.x - 0.5);
            float face = 0.34 * sqrt(oval(p, vec2(0.5, 0.5), vec2(0.265, 0.27)));
            face += 0.05 * bump(p, vec2(0.5, 0.545), vec2(0.03, 0.025));              // small nose
            face += 0.04 * bump(vec2(ax, p.y), vec2(0.165, 0.565), vec2(0.06, 0.045)); // cheeks
            face -= 0.025 * bump(vec2(ax, p.y), vec2(0.135, 0.455), vec2(0.065, 0.05)); // eye sockets
            face += 0.03 * bump(p, vec2(0.5, 0.72), vec2(0.09, 0.035));               // chin plate
            float hair = 0.24 * sqrt(oval(p, vec2(0.5, 0.42), vec2(0.38, 0.42)));
            float phones = 0.3 * bump(vec2(ax, p.y), vec2(0.29, 0.48), vec2(0.05, 0.12)); // headphones
            float bow = 0.2 * bump(p, vec2(0.67, 0.09), vec2(0.05, 0.04));
            float neck = 0.1 * sqrt(oval(p, vec2(0.5, 0.80), vec2(0.12, 0.08)));
            float body = 0.15 * sqrt(oval(p, vec2(0.5, 1.05), vec2(0.45, 0.3)));
            // The knife and the hand holding it come out of the picture, towards the camera.
            float hand = 0.3 * bump(p, vec2(0.8, 0.88), vec2(0.08, 0.08));
            float d = bladeDistance(p) / 0.045;
            float blade = 0.26 * exp(-d * d);
            return max(max(max(face, max(hair, phones)), max(bow, max(neck, body))), max(hand, blade));
        }

        // Jaw: the lower teeth and the robot chin plate drop like the jaw of a ventriloquist doll (between the corners
        // of the mouth, so that its dark outline is not dragged down there).
        float jaw(vec2 p)
        {
            float below = smoothstep(mouthLine(p.x), mouthLine(p.x) + MOUTH_GAP, p.y) * (1.0 - smoothstep(0.745, 0.79, p.y));
            float across = 1.0 - smoothstep(0.06, 0.1, abs(p.x - 0.5));
            return below * across;
        }

        // Spiral pupil: the image turns around the centre of the eye, faster near it, so that the spiral spins.
        vec2 spin(vec2 uv, vec2 eye, float angle)
        {
            vec2 d = uv - eye;
            float fall = 1.0 - smoothstep(0.018, 0.034, length(d));
            float a = angle * fall;
            return eye + mat2(cos(a), -sin(a), sin(a), cos(a)) * d;
        }

        """);

    // Robot boy: face axis at x 0.483, green irises at (0.40, 0.3825) and (0.565, 0.3825), nose tip at (0.475, 0.465),
    // closed smile along y 0.517 at the centre and 0.507 at its corners (x 0.42 to 0.545), chin at 0.605, hair from
    // 0.1, ears at x 0.30 and 0.675, robot neck and white suit below with its lights (the core at (0.5, 0.762)).
    public static readonly FaceModel MusukoFace = new("avares://KAONOMI.Chat/Assets/musuko-face.png", """
        #define MUSUKO
        const vec2 EYE_LEFT = vec2(0.40, 0.3825);
        const vec2 EYE_RIGHT = vec2(0.565, 0.3825);
        const float MOUTH_X = 0.483;
        const float MOUTH_HALF = 0.058;
        const float MOUTH_GAP = 0.01;
        const float JAW_DROP = 0.035;
        const vec3 MOUTH_DARK = vec3(0.10, 0.04, 0.08);
        const vec3 MOUTH_LIGHT = vec3(0.55, 0.28, 0.32);   // the tongue

        """, """
        float mouthLine(float x)
        {
            float d = (x - 0.483) / 0.065;
            return 0.517 - 0.01 * d * d;
        }

        float relief(vec2 p)
        {
            float ax = abs(p.x - 0.483);
            float head = 0.34 * sqrt(oval(p, vec2(0.485, 0.42), vec2(0.175, 0.2)));
            head += 0.07 * bump(p, vec2(0.475, 0.462), vec2(0.03, 0.025));              // nose tip
            head += 0.03 * bump(p, vec2(0.48, 0.42), vec2(0.02, 0.05));                 // nose bridge
            head += 0.035 * bump(vec2(ax, p.y), vec2(0.1, 0.46), vec2(0.05, 0.04));     // cheeks
            head -= 0.025 * bump(vec2(ax, p.y), vec2(0.083, 0.383), vec2(0.045, 0.025)); // eye sockets
            head += 0.02 * bump(vec2(ax, p.y), vec2(0.085, 0.33), vec2(0.06, 0.02));    // brows
            head += 0.03 * bump(p, vec2(0.483, 0.575), vec2(0.07, 0.035));              // chin
            float ears = 0.12 * bump(vec2(ax, p.y), vec2(0.19, 0.44), vec2(0.025, 0.045));
            float hair = 0.27 * sqrt(oval(p, vec2(0.49, 0.29), vec2(0.215, 0.21)));
            float neck = 0.1 * sqrt(oval(p, vec2(0.5, 0.65), vec2(0.1, 0.07)));
            float body = 0.16 * sqrt(oval(p, vec2(0.5, 1.08), vec2(0.4, 0.4)));
            return max(max(max(head, ears), hair), max(neck, body));
        }

        // Jaw: below the line of the mouth, down to the chin, drops with the opening of the mouth.
        float jaw(vec2 p)
        {
            float below = smoothstep(mouthLine(p.x), mouthLine(p.x) + MOUTH_GAP, p.y) * (1.0 - smoothstep(0.59, 0.625, p.y));
            float across = 1.0 - smoothstep(0.1, 0.15, abs(p.x - 0.483));
            return below * across;
        }

        // Light of one of the glowing lights of the suit.
        float light(vec2 p, vec2 center, float size)
        {
            return exp(-length(p - center) / size);
        }

        """);

    // Masked robot: face axis at x 0.5, head from y 0.04 to the chin at 0.775 (x 0.23 to 0.775), eye holes at
    // (0.335, 0.37) and (0.668, 0.37), nostrils at y 0.567, mouth slot from x 0.44 to 0.565 and y 0.625 to 0.655, ear
    // discs at x 0.155 and 0.845, neck and collar below 0.78, shoulders on the edges.
    public static readonly FaceModel KaonamiFace = new("avares://KAONOMI.Chat/Assets/kaonami-face.png", """
        #define KAONAMI
        const vec2 EYE_LEFT = vec2(0.335, 0.37);
        const vec2 EYE_RIGHT = vec2(0.668, 0.37);
        const vec2 SLOT = vec2(0.503, 0.64);
        const float MOUTH_X = 0.503;
        const float MOUTH_HALF = 0.055;
        const float MOUTH_GAP = 0.01;
        const float JAW_DROP = 0.035;
        const vec3 MOUTH_DARK = vec3(0.02, 0.02, 0.02);
        const vec3 MOUTH_LIGHT = vec3(0.35, 0.16, 0.05);   // the glow inside

        """, """
        // Bottom edge of the mouth slot.
        float mouthLine(float x)
        {
            return 0.653;
        }

        float relief(vec2 p)
        {
            float ax = abs(p.x - 0.5);
            float head = 0.36 * sqrt(oval(p, vec2(0.5, 0.42), vec2(0.275, 0.36)));
            head -= 0.03 * bump(vec2(ax, p.y), vec2(0.167, 0.37), vec2(0.04, 0.04));   // eye holes
            head += 0.03 * bump(p, vec2(0.5, 0.71), vec2(0.1, 0.05));                  // chin
            float ears = 0.2 * bump(vec2(ax, p.y), vec2(0.345, 0.38), vec2(0.06, 0.1)); // ear discs
            float neck = 0.1 * sqrt(oval(p, vec2(0.5, 0.82), vec2(0.15, 0.08)));
            float body = 0.16 * sqrt(oval(p, vec2(0.5, 1.1), vec2(0.55, 0.32)));
            return max(max(head, ears), max(neck, body));
        }

        // Jaw: the chin under the mouth slot drops like a flap.
        float jaw(vec2 p)
        {
            float below = smoothstep(mouthLine(p.x), mouthLine(p.x) + MOUTH_GAP, p.y) * (1.0 - smoothstep(0.75, 0.79, p.y));
            float across = 1.0 - smoothstep(0.08, 0.14, abs(p.x - 0.503));
            return below * across;
        }

        """);

    // Android child, face front: face axis at x 0.495, head cut by the top of the frame at y 0.083 (x 0.05 to 0.95 with
    // the ears), glowing green eyes at (0.30, 0.425) and (0.71, 0.425), brows at 0.29, nose tip at (0.498, 0.625), closed
    // lips along y 0.738 at the centre and 0.731 at their corners (x 0.418 to 0.569), chin at 0.8, green mechanical ears at
    // x 0.08 and 0.92, neck and shoulders below, cut by the bottom of the frame at 0.92.
    public static readonly FaceModel GreenFace = new("avares://KAONOMI.Chat/Assets/green-face.png", """
        #define GREEN
        const vec2 EYE_LEFT = vec2(0.30, 0.425);
        const vec2 EYE_RIGHT = vec2(0.71, 0.425);
        const float MOUTH_X = 0.495;
        const float MOUTH_HALF = 0.078;
        const float MOUTH_GAP = 0.01;
        const float JAW_DROP = 0.035;
        const vec3 MOUTH_DARK = vec3(0.02, 0.05, 0.04);
        const vec3 MOUTH_LIGHT = vec3(0.12, 0.45, 0.2);   // a green glow inside

        """, """
        float mouthLine(float x)
        {
            float d = (x - 0.495) / 0.075;
            return 0.738 - 0.007 * d * d;
        }

        float relief(vec2 p)
        {
            float ax = abs(p.x - 0.495);
            float head = 0.4 * sqrt(oval(p, vec2(0.5, 0.42), vec2(0.4, 0.45)));
            head += 0.08 * bump(p, vec2(0.498, 0.615), vec2(0.04, 0.035));               // nose tip
            head += 0.035 * bump(p, vec2(0.497, 0.53), vec2(0.025, 0.07));               // nose bridge
            head += 0.04 * bump(vec2(ax, p.y), vec2(0.2, 0.6), vec2(0.08, 0.06));        // cheeks
            head -= 0.035 * bump(vec2(ax, p.y), vec2(0.21, 0.425), vec2(0.09, 0.045));   // eye sockets
            head += 0.025 * bump(vec2(ax, p.y), vec2(0.2, 0.3), vec2(0.1, 0.025));       // brows
            head += 0.03 * bump(p, vec2(0.497, 0.775), vec2(0.08, 0.035));               // chin
            float ears = 0.16 * bump(vec2(ax, p.y), vec2(0.42, 0.47), vec2(0.04, 0.09));
            float neck = 0.1 * sqrt(oval(p, vec2(0.5, 0.88), vec2(0.15, 0.08)));
            float body = 0.16 * sqrt(oval(p, vec2(0.5, 1.12), vec2(0.6, 0.28)));
            return max(max(head, ears), max(neck, body));
        }

        // Jaw: below the lips, down to the chin, drops with the opening of the mouth.
        float jaw(vec2 p)
        {
            float below = smoothstep(mouthLine(p.x), mouthLine(p.x) + MOUTH_GAP, p.y) * (1.0 - smoothstep(0.8, 0.835, p.y));
            float across = 1.0 - smoothstep(0.12, 0.18, abs(p.x - 0.495));
            return below * across;
        }

        """);

    // Circuit: an android woman (a photo cut out of its white background): eyes at (0.414, 0.398) and (0.584, 0.399), nose
    // tip at (0.498, 0.51), lips slightly apart along y 0.59 (x 0.439 to 0.557), chin at 0.69, face from x 0.32 to 0.68,
    // black hair parted in the middle from y 0.05, transparent circuit panels on her temples, circuit collar from 0.65 and
    // shoulders below, faded out above the bottom of the original (0.82). A photo that lives (LIVE: her jaw drops with
    // the voice, she blinks), and the green circuits glow with the voice.
    public static readonly FaceModel CircuitFace = new("avares://KAONOMI.Chat/Assets/circuit-face.png", """
        #define LIVE
        #define CIRCUIT
        const vec2 EYE_LEFT = vec2(0.414, 0.398);
        const vec2 EYE_RIGHT = vec2(0.584, 0.399);
        const vec2 EYE_SIZE = vec2(0.058, 0.03);    // half size of the eyelid, past the corners and the lashes
        const float MOUTH_X = 0.499;
        const float MOUTH_HALF = 0.052;
        const float MOUTH_GAP = 0.01;
        const float JAW_DROP = 0.03;
        const vec3 MOUTH_DARK = vec3(0.12, 0.03, 0.04);
        const vec3 MOUTH_LIGHT = vec3(0.6, 0.28, 0.3);   // the tongue

        """, """
        float mouthLine(float x)
        {
            float u = x - 0.499;
            return 0.59 - 0.3 * u * u;
        }

        float relief(vec2 p)
        {
            float ax = abs(p.x - 0.497);
            float head = 0.3 * sqrt(oval(p, vec2(0.497, 0.43), vec2(0.18, 0.27)));
            head += 0.06 * bump(p, vec2(0.498, 0.51), vec2(0.022, 0.026));              // nose tip
            head += 0.025 * bump(p, vec2(0.498, 0.45), vec2(0.015, 0.05));              // nose bridge
            head += 0.03 * bump(vec2(ax, p.y), vec2(0.11, 0.51), vec2(0.05, 0.045));    // cheeks
            head -= 0.02 * bump(vec2(ax, p.y), vec2(0.085, 0.398), vec2(0.04, 0.022));  // eye sockets
            head += 0.02 * bump(p, vec2(0.499, 0.6), vec2(0.035, 0.015));               // lips
            head += 0.025 * bump(p, vec2(0.499, 0.67), vec2(0.06, 0.025));              // chin
            float hair = 0.24 * sqrt(oval(p, vec2(0.497, 0.3), vec2(0.21, 0.25)));
            float neck = 0.1 * sqrt(oval(p, vec2(0.497, 0.73), vec2(0.12, 0.08)));
            float body = 0.14 * sqrt(oval(p, vec2(0.5, 1.0), vec2(0.5, 0.26)));
            return max(max(head, hair), max(neck, body));
        }

        // Jaw: below the lips, down to the chin, drops with the opening of the mouth.
        float jaw(vec2 p)
        {
            float below = smoothstep(mouthLine(p.x), mouthLine(p.x) + MOUTH_GAP, p.y) * (1.0 - smoothstep(0.67, 0.71, p.y));
            float across = 1.0 - smoothstep(0.1, 0.15, abs(p.x - 0.499));
            return below * across;
        }

        """);

    // Motuko: an android woman (a photo cut out of its white background): eyes at (0.425, 0.402) and (0.574, 0.399), nose
    // tip at (0.499, 0.5), closed lips along y 0.562 (x 0.447 to 0.556), chin at 0.642, face from x 0.33 to 0.66, dark hair
    // from y 0.08 down to the jaw on both sides, high white collar from 0.61, white armour shoulders and chest below, cut
    // by the bottom and the sides of the frame. A photo that lives (LIVE: her jaw drops with the voice, she blinks),
    // and the amber lights of the armour glow.
    public static readonly FaceModel MotukoFace = new("avares://KAONOMI.Chat/Assets/motuko-face.png", """
        #define LIVE
        #define MOTUKO
        const vec2 EYE_LEFT = vec2(0.425, 0.402);
        const vec2 EYE_RIGHT = vec2(0.574, 0.399);
        const vec2 EYE_SIZE = vec2(0.051, 0.024);   // half size of the eyelid, past the corners and the lashes
        const float MOUTH_X = 0.502;
        const float MOUTH_HALF = 0.05;
        const float MOUTH_GAP = 0.009;
        const float JAW_DROP = 0.028;
        const vec3 MOUTH_DARK = vec3(0.14, 0.04, 0.05);
        const vec3 MOUTH_LIGHT = vec3(0.62, 0.3, 0.32);   // the tongue

        """, """
        float mouthLine(float x)
        {
            float u = x - 0.502;
            return 0.563 - 0.3 * u * u;
        }

        float relief(vec2 p)
        {
            float ax = abs(p.x - 0.5);
            float head = 0.3 * sqrt(oval(p, vec2(0.5, 0.42), vec2(0.17, 0.27)));
            head += 0.06 * bump(p, vec2(0.499, 0.5), vec2(0.02, 0.024));                // nose tip
            head += 0.025 * bump(p, vec2(0.499, 0.445), vec2(0.014, 0.05));             // nose bridge
            head += 0.03 * bump(vec2(ax, p.y), vec2(0.1, 0.5), vec2(0.045, 0.04));      // cheeks
            head -= 0.02 * bump(vec2(ax, p.y), vec2(0.075, 0.4), vec2(0.035, 0.02));    // eye sockets
            head += 0.02 * bump(p, vec2(0.502, 0.57), vec2(0.03, 0.012));               // lips
            head += 0.025 * bump(p, vec2(0.502, 0.625), vec2(0.06, 0.025));             // chin
            float hair = 0.25 * sqrt(oval(p, vec2(0.5, 0.32), vec2(0.23, 0.25)));
            float neck = 0.1 * sqrt(oval(p, vec2(0.5, 0.68), vec2(0.09, 0.07)));
            float collar = 0.14 * sqrt(oval(p, vec2(0.5, 0.72), vec2(0.17, 0.12)));
            float body = 0.17 * sqrt(oval(p, vec2(0.5, 1.05), vec2(0.6, 0.36)));
            float shoulders = 0.08 * bump(vec2(ax, p.y), vec2(0.42, 0.9), vec2(0.1, 0.12));
            return max(max(max(head, hair), max(neck, collar)), body + shoulders);
        }

        // Jaw: below the lips, down to the chin, drops with the opening of the mouth.
        float jaw(vec2 p)
        {
            float below = smoothstep(mouthLine(p.x), mouthLine(p.x) + MOUTH_GAP, p.y) * (1.0 - smoothstep(0.62, 0.66, p.y));
            float across = 1.0 - smoothstep(0.09, 0.14, abs(p.x - 0.502));
            return below * across;
        }

        """);

    // Kaonomi robot (a photo cut out of a rainy street): egg-shaped head centred at (0.506, 0.358) (x 0.386 to 0.626, y
    // 0.205 to 0.51) in a hood (x 0.22 to 0.78, from y 0.13), its single yellow eye at (0.555, 0.33) in a ring of radius
    // 0.054, cables at the neck, a wide chest plate from y 0.5, arms on the sides, legs cut by the bottom of the frame.
    // No mouth: the eye speaks.
    public static readonly FaceModel KaonomiRobotFace = new("avares://KAONOMI.Chat/Assets/kaonomi-robot-face.png", """
        #define KROBOT
        const vec2 EYE = vec2(0.555, 0.33);
        const float EYE_RING = 0.054;
        const float MOUTH_X = 0.5;
        const float MOUTH_HALF = 0.0;
        const float MOUTH_GAP = 0.01;
        const float JAW_DROP = 0.0;
        const vec3 MOUTH_DARK = vec3(0.0);
        const vec3 MOUTH_LIGHT = vec3(0.0);

        """, """
        float mouthLine(float x)
        {
            return 0.6;
        }

        float relief(vec2 p)
        {
            float ax = abs(p.x - 0.5);
            // The egg-shaped head stands out of its hood; the eye ring is set into it.
            float head = 0.38 * sqrt(oval(p, vec2(0.506, 0.358), vec2(0.12, 0.155)));
            head += 0.025 * bump(p, EYE, vec2(EYE_RING, EYE_RING));
            float hood = 0.26 * sqrt(oval(p, vec2(0.5, 0.42), vec2(0.29, 0.3)));
            float chest = 0.24 * sqrt(oval(p, vec2(0.5, 0.78), vec2(0.3, 0.26)));
            float arms = 0.16 * bump(vec2(ax, p.y), vec2(0.34, 0.7), vec2(0.07, 0.22));
            return max(max(head, hood), max(chest, arms));
        }

        // No jaw: the robot has no mouth.
        float jaw(vec2 p)
        {
            return 0.0;
        }

        """);

    // Rivet, a worn tin robot in black and white, its head turned a little (its edges tilted about 9°, rising to the
    // right): a square head box from the top of the frame (an antenna cut by it at x 0.39) to y 0.54 on the left and 0.47
    // on the right, two goggle eyes, their white eyeballs at (0.317, 0.378) and (0.576, 0.325) (radius 0.057, the
    // pupils 0.028) in rings of radius 0.115, bolts on the sides (0.16, 0.43) and (0.78, 0.33); under a dark seam, a box
    // for a jaw from x 0.21 to 0.80, its top along jawTop(x) (0.573 on the left, 0.477 on the right, curved by the
    // perspective) and 0.158 high, with a mouth slot at (0.496, 0.610); a spring neck from 0.67 to 0.81, the round body
    // below, cut by the bottom of the frame.
    public static readonly FaceModel RivetFace = new("avares://KAONOMI.Chat/Assets/rivet-face.png", """
        #define RIVET
        const vec2 EYE_LEFT = vec2(0.317, 0.378);
        const vec2 EYE_RIGHT = vec2(0.576, 0.325);
        const float EYE_R = 0.057;
        const vec2 RING_LEFT = vec2(0.311, 0.367);
        const vec2 RING_RIGHT = vec2(0.587, 0.31);
        const float RING_R = 0.115;
        const vec2 SLOT = vec2(0.496, 0.61);
        const float MOUTH_X = 0.505;      // the jaw box, across its width
        const float MOUTH_HALF = 0.29;
        const float MOUTH_GAP = 0.009;
        const float JAW_DROP = 0.07;
        const vec3 MOUTH_DARK = vec3(0.01, 0.01, 0.012);
        const vec3 MOUTH_LIGHT = vec3(0.13, 0.14, 0.15);   // the inside of the box, faintly lit

        """, """
        // Top edge of the jaw box: through (0.244, 0.5725), (0.5615, 0.5066) and (0.764, 0.4773).
        float jawTop(float x)
        {
            return 0.5725 - 0.2076 * (x - 0.2441) + 0.1211 * (x - 0.2441) * (x - 0.5615);
        }

        // The seam: a little above the jaw box, in the dark gap under the head, so that only that gap opens.
        float mouthLine(float x)
        {
            return jawTop(x) - 0.012;
        }

        // Coordinates along the tilt of the head: x along its edges, y across them (down).
        vec2 tilted(vec2 p, vec2 centre)
        {
            vec2 d = p - centre;
            return vec2(dot(d, vec2(0.987, -0.16)), dot(d, vec2(0.16, 0.987)));
        }

        // A flat plate of half size h along the tilt, its edges rounded over soft.
        float plate(vec2 p, vec2 centre, vec2 h, float soft)
        {
            vec2 q = abs(tilted(p, centre));
            return (1.0 - smoothstep(h.x - soft, h.x, q.x)) * (1.0 - smoothstep(h.y - soft, h.y, q.y));
        }

        float relief(vec2 p)
        {
            // The head and the jaw: two boxes, flat, rounded at their edges.
            float head = 0.3 * plate(p, vec2(0.44, 0.285), vec2(0.285, 0.225), 0.06);
            float chin = 0.31 * plate(p, vec2(0.503, 0.606), vec2(0.29, 0.08), 0.04);
            // The goggles stand out of the head: their rims, and the glass of the eyes in them.
            float rl = (length(p - RING_LEFT) - RING_R) / 0.015;
            float rr = (length(p - RING_RIGHT) - RING_R) / 0.015;
            head += 0.035 * (exp(-rl * rl) + exp(-rr * rr)) + 0.03 * (bump(p, EYE_LEFT, vec2(EYE_R)) + bump(p, EYE_RIGHT, vec2(EYE_R)));
            float bolts = 0.27 * (bump(p, vec2(0.16, 0.43), vec2(0.04, 0.035)) + bump(p, vec2(0.78, 0.33), vec2(0.035, 0.05)));
            // The antenna at the depth of the top of the head, as wide as its rod and more (a narrow bump would make a spike).
            float antenna = 0.28 * (1.0 - smoothstep(0.03, 0.05, abs(p.x - 0.39))) * (1.0 - smoothstep(0.07, 0.1, p.y));
            float neck = 0.14 * sqrt(oval(p, vec2(0.57, 0.765), vec2(0.12, 0.08)));
            float body = 0.22 * sqrt(oval(p, vec2(0.62, 1.08), vec2(0.42, 0.28)));
            return max(max(max(head, chin), max(bolts, antenna)), max(neck, body));
        }

        // Jaw: the whole box under the seam drops; its spring neck takes up the drop (squeezed), the body stays.
        float jaw(vec2 p)
        {
            float below = smoothstep(mouthLine(p.x), mouthLine(p.x) + MOUTH_GAP, p.y);
            float bottom = 0.7344 - 0.1745 * (p.x - 0.2363);
            float down = 1.0 - smoothstep(bottom, 0.81, p.y);
            float across = smoothstep(0.12, 0.17, p.x) * (1.0 - smoothstep(0.83, 0.88, p.x));
            return below * down * across;
        }

        """);

    // Shiro, a drawn android boy, face front: face axis at x 0.5, face from the hair (y 0.26) to the chin at (0.506, 0.646),
    // x 0.345 to 0.655 at the cheeks; eye openings centred at (0.393, 0.418) and (0.599, 0.419) (half size 0.05 × 0.03),
    // green irises at (0.404, 0.415) and (0.592, 0.418) (radius 0.024), brows at 0.36, nose tip at (0.5, 0.49), closed
    // lips along y 0.568 (x 0.454 to 0.546), a seam down the forehead at x 0.4815 from 0.262 to the nose (0.51); black
    // hair from the top of the frame to 0.32 (x 0.24 to 0.76), pointed ears at x 0.3 and 0.7, a mechanical neck from 0.6,
    // a white armour from 0.78, cut by the bottom of the frame.
    public static readonly FaceModel ShiroFace = new("avares://KAONOMI.Chat/Assets/shiro-face.png", """
        #define SHIRO
        const vec2 EYE_LEFT = vec2(0.3926, 0.418);   // the openings of the eyes
        const vec2 EYE_RIGHT = vec2(0.5986, 0.419);
        const vec2 EYE_SIZE = vec2(0.05, 0.03);
        const vec2 IRIS_LEFT = vec2(0.4043, 0.415);
        const vec2 IRIS_RIGHT = vec2(0.5918, 0.418);
        const float IRIS_R = 0.024;
        const float SEAM_X = 0.4815;      // the seam of the forehead (its top slants to x 0.4705)
        const float SEAM_TOP = 0.262;
        const float SEAM_BOTTOM = 0.51;
        const float MOUTH_X = 0.5;
        const float MOUTH_HALF = 0.046;
        const float MOUTH_GAP = 0.01;
        const float JAW_DROP = 0.045;
        const vec3 MOUTH_DARK = vec3(0.14, 0.09, 0.11);
        const vec3 MOUTH_LIGHT = vec3(0.55, 0.32, 0.35);   // the tongue

        """, """
        // The line of the closed lips, a little lower at their corners.
        float mouthLine(float x)
        {
            float d = (x - 0.5) / 0.046;
            return 0.5683 + 0.003 * d * d;
        }

        float relief(vec2 p)
        {
            float ax = abs(p.x - 0.5);
            float face = 0.34 * sqrt(oval(p, vec2(0.5, 0.45), vec2(0.165, 0.2)));
            face += 0.05 * bump(p, vec2(0.505, 0.49), vec2(0.022, 0.035));              // nose
            face += 0.02 * bump(p, vec2(0.5, 0.45), vec2(0.02, 0.05));                  // nose bridge
            face -= 0.02 * bump(vec2(ax, p.y), vec2(0.103, 0.418), vec2(0.05, 0.03));   // eye sockets
            face += 0.03 * bump(vec2(ax, p.y), vec2(0.12, 0.5), vec2(0.05, 0.04));      // cheeks
            face += 0.02 * bump(p, vec2(0.505, 0.625), vec2(0.05, 0.03));               // chin
            float hair = 0.26 * sqrt(oval(p, vec2(0.5, 0.24), vec2(0.27, 0.26)));
            float ears = 0.22 * bump(vec2(ax, p.y), vec2(0.2, 0.43), vec2(0.035, 0.08));
            float neck = 0.12 * sqrt(oval(p, vec2(0.5, 0.74), vec2(0.13, 0.12)));
            float body = 0.2 * sqrt(oval(p, vec2(0.5, 1.15), vec2(0.56, 0.38)));
            return max(max(face, hair), max(max(ears, neck), body));
        }

        // Jaw: below the lips, across the chin, the face drops; the neck under the chin takes up the drop.
        float jaw(vec2 p)
        {
            float below = smoothstep(mouthLine(p.x), mouthLine(p.x) + MOUTH_GAP, p.y) * (1.0 - smoothstep(0.645, 0.7, p.y));
            float across = 1.0 - smoothstep(0.1, 0.16, abs(p.x - 0.5));
            return below * across;
        }

        """);

    // Kingyo, a golden robot fish seen from the side, its head to the left: the body from x 0.105 (the mouth) to 0.75
    // (the root of the tail) and y 0.29 to 0.66; its mouth a seam from the front (0.107, 0.516) down to (0.241, 0.607),
    // the lower lip under it; the eye (a white ball of radius 0.03, its pupil 0.016) at (0.25, 0.492) in an orange ring;
    // the silver middle of the body with a motor whose end cap (an ellipse of radii 0.0215 × 0.041) is at (0.635, 0.502);
    // the dorsal fin above y 0.31 (x 0.36 to 0.58), the tail from x 0.75 to 0.94, the fins under the belly below 0.665.
    public static readonly FaceModel KingyoFace = new("avares://KAONOMI.Chat/Assets/kingyo-face.png", """
        #define KINGYO
        const vec2 EYE = vec2(0.25, 0.492);
        const float EYE_R = 0.03;
        const vec2 CAP = vec2(0.6353, 0.502);
        const vec2 CAP_R = vec2(0.027, 0.05);   // a little wider than the cap: its edge turns too
        const vec2 MOUTH_FRONT = vec2(0.105, 0.528);
        const float MOUTH_X = 0.165;      // along the seam, the front open the most
        const float MOUTH_HALF = 0.065;
        const float MOUTH_GAP = 0.01;
        const float JAW_DROP = 0.05;
        const vec3 MOUTH_DARK = vec3(0.1, 0.04, 0.02);
        const vec3 MOUTH_LIGHT = vec3(0.65, 0.28, 0.08);   // a warm light inside

        """, """
        // The seam of the mouth: through (0.1074, 0.5156), (0.1855, 0.5615) and (0.2412, 0.6074).
        float mouthLine(float x)
        {
            return 0.5156 + 0.5877 * (x - 0.1074) + 1.766 * (x - 0.1074) * (x - 0.1855);
        }

        float relief(vec2 p)
        {
            float body = 0.3 * sqrt(oval(p, vec2(0.43, 0.475), vec2(0.33, 0.19)));
            body += 0.06 * bump(p, vec2(0.24, 0.49), vec2(0.12, 0.13));      // the round head
            body += 0.03 * bump(p, EYE, vec2(0.045));                       // the eye in its ring
            body += 0.05 * bump(p, vec2(0.64, 0.5), vec2(0.08, 0.08));       // the motor
            // The fins, thin: they stay nearly flat.
            float dorsal = 0.14 * sqrt(oval(p, vec2(0.47, 0.24), vec2(0.12, 0.1)));
            float tail = 0.08 * sqrt(oval(p, vec2(0.85, 0.43), vec2(0.13, 0.25)));
            float fins = 0.07 * sqrt(oval(p, vec2(0.53, 0.74), vec2(0.2, 0.12)));
            return max(max(body, dorsal), max(tail, fins));
        }

        // Jaw: the lower lip under the seam drops, the most at the front (its hinge is behind the mouth).
        float jaw(vec2 p)
        {
            float line = mouthLine(p.x);
            float below = smoothstep(line, line + MOUTH_GAP, p.y) * (1.0 - smoothstep(line + 0.05, line + 0.09, p.y));
            float front = 1.0 - smoothstep(0.12, 0.27, p.x);
            return below * front;
        }

        // Bubble k (0 to 3) rising from the mouth at p: its ring (x) and its glint (y). Each lives 3.2 s, a quarter of
        // that after the one before, and drifts on its own way up.
        vec2 bubble(vec2 p, float k, float time)
        {
            float age = time / 3.2 + k * 0.25;
            float life = fract(age);
            float drift = fract(sin((mod(floor(age), 89.0) + k * 7.0) * 12.9898) * 43758.5453);
            vec2 centre = MOUTH_FRONT + vec2(-0.015 - 0.04 * life + 0.01 * sin(life * 9.0 + drift * 6.0), -0.02 - 0.3 * life);
            float radius = 0.006 + 0.012 * life + 0.006 * drift;
            float r = length(p - centre) / radius;
            float fade = smoothstep(0.0, 0.1, life) * (1.0 - smoothstep(0.7, 1.0, life));
            float ring = exp(-pow((r - 1.0) / 0.2, 2.0)) + 0.12 * (1.0 - smoothstep(0.8, 1.0, r));
            float glint = exp(-pow(length(p - centre + radius * vec2(0.35, 0.35)) / (radius * 0.25), 2.0));
            return vec2(ring, glint) * fade;
        }

        """);

    // Hikari, an android girl, face front (a photo): face axis at x 0.518, face from the hairline (y 0.1) to the chin at
    // 0.649, x 0.32 to 0.71; eyes (white and iris) centred at (0.404, 0.38) and (0.636, 0.383), half size 0.057 × 0.035,
    // green irises of radius 0.035 at (0.406, 0.38) and (0.632, 0.383); brows down to y 0.3, the skin between brows and
    // lashes at 0.318; nose tip at (0.528, 0.48); smiling lips parted along y 0.5576 at the centre and 0.543 at their
    // corners (x 0.4565 to 0.5835); a stone on the forehead at (0.535, 0.093), a green light on the right shoulder at
    // (0.879, 0.862); long blond hair on both sides down to the bottom, a headset over it, a robot neck from 0.65 and an
    // armour from 0.78, cut by the bottom of the frame (the hair also by its top).
    public static readonly FaceModel HikariFace = new("avares://KAONOMI.Chat/Assets/hikari-face.png", """
        #define HIKARI
        const vec2 EYE_LEFT = vec2(0.4043, 0.38);
        const vec2 EYE_RIGHT = vec2(0.6357, 0.3828);
        const vec2 EYE_SIZE = vec2(0.07, 0.045);   // half size of the eyelid, past the corners of the eye
        const vec2 IRIS_LEFT = vec2(0.406, 0.38);
        const vec2 IRIS_RIGHT = vec2(0.632, 0.383);
        const float IRIS_R = 0.035;
        const vec2 STONE = vec2(0.535, 0.093);
        const vec2 LED = vec2(0.879, 0.862);
        const float MOUTH_X = 0.5176;
        const float MOUTH_HALF = 0.06;
        const float MOUTH_GAP = 0.01;
        const float JAW_DROP = 0.05;
        const vec3 MOUTH_DARK = vec3(0.28, 0.08, 0.1);
        const vec3 MOUTH_LIGHT = vec3(0.85, 0.45, 0.47);   // the tongue

        """, """
        // The parting of the lips: a smile, its corners higher.
        float mouthLine(float x)
        {
            float d = (x - 0.5176) / 0.0635;
            return 0.5576 - 0.0144 * d * d;
        }

        float relief(vec2 p)
        {
            float ax = abs(p.x - 0.518);
            float face = 0.34 * sqrt(oval(p, vec2(0.517, 0.4), vec2(0.195, 0.25)));
            face += 0.05 * bump(p, vec2(0.528, 0.48), vec2(0.03, 0.045));               // nose
            face += 0.02 * bump(p, vec2(0.522, 0.4), vec2(0.022, 0.06));                // nose bridge
            face -= 0.02 * bump(vec2(ax, p.y), vec2(0.115, 0.38), vec2(0.06, 0.035));   // eye sockets
            face += 0.03 * bump(vec2(ax, p.y), vec2(0.12, 0.47), vec2(0.06, 0.05));     // cheeks
            face += 0.02 * bump(p, vec2(0.518, 0.62), vec2(0.06, 0.03));                // chin
            float hair = 0.22 * sqrt(oval(p, vec2(0.52, 0.45), vec2(0.4, 0.55)));
            float headset = 0.24 * bump(vec2(ax, p.y), vec2(0.31, 0.32), vec2(0.04, 0.12));
            float neck = 0.12 * sqrt(oval(p, vec2(0.5, 0.75), vec2(0.12, 0.12)));
            float armour = 0.2 * sqrt(oval(p, vec2(0.5, 1.1), vec2(0.45, 0.3)));
            return max(max(face, hair), max(headset, max(neck, armour)));
        }

        // Jaw: below the lips, across the chin, the face drops; the robot neck under the chin takes up the drop.
        float jaw(vec2 p)
        {
            float below = smoothstep(mouthLine(p.x), mouthLine(p.x) + MOUTH_GAP, p.y) * (1.0 - smoothstep(0.645, 0.69, p.y));
            float across = 1.0 - smoothstep(0.1, 0.17, abs(p.x - 0.518));
            return below * across;
        }

        """);

    // Kamen, a mask alone (its strands of hair are left out, a digital rain stands for them): face axis at x 0.505, from
    // y 0.066 to the chin at 0.93, x 0.25 to 0.76; eye holes centred at (0.335, 0.509) and (0.674, 0.509) (half size
    // 0.072 × 0.025); red marks on the forehead and the cheeks; nose at (0.5, 0.7); an open mouth hole centred at
    // (0.504, 0.797) (half size 0.08 × 0.019); a small red light under the chin at (0.499, 0.912).
    public static readonly FaceModel KamenFace = new("avares://KAONOMI.Chat/Assets/kamen-face.png", """
        #define KAMEN
        const vec2 EYE_LEFT = vec2(0.3354, 0.5088);
        const vec2 EYE_RIGHT = vec2(0.6738, 0.5088);
        const vec2 MOUTH = vec2(0.5044, 0.797);
        const vec2 CHIN_LIGHT = vec2(0.499, 0.912);
        const float MOUTH_X = 0.5044;
        const float MOUTH_HALF = 0.075;
        const float MOUTH_GAP = 0.01;
        const float JAW_DROP = 0.045;
        const vec3 MOUTH_DARK = vec3(0.0);
        const vec3 MOUTH_LIGHT = vec3(0.35, 0.02, 0.04);   // a red glow deep inside

        """, """
        // The middle of the mouth hole: its lower half, the lip and the chin drop, the hole widens in the dark.
        float mouthLine(float x)
        {
            return 0.797;
        }

        float relief(vec2 p)
        {
            float mask = 0.36 * sqrt(oval(p, vec2(0.505, 0.5), vec2(0.26, 0.44)));
            mask += 0.05 * bump(p, vec2(0.5, 0.7), vec2(0.04, 0.05));                                         // nose
            mask += 0.02 * bump(p, vec2(0.505, 0.45), vec2(0.2, 0.04));                                        // brows
            mask -= 0.03 * (bump(p, EYE_LEFT, vec2(0.07, 0.025)) + bump(p, EYE_RIGHT, vec2(0.07, 0.025)));    // eye holes
            mask -= 0.03 * bump(p, MOUTH, vec2(0.08, 0.02));                                                  // mouth hole
            mask += 0.02 * bump(p, vec2(0.505, 0.88), vec2(0.08, 0.04));                                       // chin
            return mask;
        }

        // Jaw: below the middle of the mouth hole, across the chin, the mask drops.
        float jaw(vec2 p)
        {
            float below = smoothstep(mouthLine(p.x), mouthLine(p.x) + MOUTH_GAP, p.y) * (1.0 - smoothstep(0.93, 0.97, p.y));
            float across = 1.0 - smoothstep(0.14, 0.21, abs(p.x - MOUTH_X));
            return below * across;
        }

        """);

    // Aoi, a head of glass full of blue circuits, face front: face axis at x 0.497, from y 0.06 to the chin at 0.94, x 0.22
    // to 0.78 (0.16 to 0.84 with the ears); closed glowing eyes at (0.356, 0.493) and (0.64, 0.488) (half size about
    // 0.065 × 0.025); nose tip at (0.53, 0.635); closed lips parted along y 0.7795, x 0.39 to 0.6.
    public static readonly FaceModel AoiFace = new("avares://KAONOMI.Chat/Assets/aoi-face.png", """
        #define AOI
        const vec2 EYE_LEFT = vec2(0.356, 0.493);
        const vec2 EYE_RIGHT = vec2(0.64, 0.488);
        const vec2 EYE_HALF = vec2(0.065, 0.025);
        const float MOUTH_X = 0.495;
        const float MOUTH_HALF = 0.07;
        const float MOUTH_GAP = 0.01;
        const float JAW_DROP = 0.045;
        const vec3 MOUTH_DARK = vec3(0.03, 0.14, 0.32);
        const vec3 MOUTH_LIGHT = vec3(0.45, 0.88, 1.0);   // a cyan glow inside

        """, """
        // The parting of the lips, nearly straight.
        float mouthLine(float x)
        {
            float d = (x - MOUTH_X) / 0.105;
            return 0.7795 - 0.004 * d * d;
        }

        float relief(vec2 p)
        {
            float ax = abs(p.x - 0.497);
            float head = 0.36 * sqrt(oval(p, vec2(0.497, 0.5), vec2(0.29, 0.44)));
            head += 0.05 * bump(p, vec2(0.53, 0.635), vec2(0.04, 0.05));                // nose
            head += 0.02 * bump(p, vec2(0.5, 0.55), vec2(0.025, 0.07));                 // nose bridge
            head -= 0.02 * bump(vec2(ax, p.y), vec2(0.142, 0.49), vec2(0.065, 0.03));   // eye sockets
            head += 0.03 * bump(vec2(ax, p.y), vec2(0.15, 0.62), vec2(0.07, 0.06));     // cheeks
            head += 0.02 * bump(p, vec2(0.497, 0.78), vec2(0.1, 0.03));                 // lips
            head += 0.02 * bump(p, vec2(0.497, 0.89), vec2(0.08, 0.04));                // chin
            float ears = 0.24 * bump(vec2(ax, p.y), vec2(0.31, 0.5), vec2(0.035, 0.08));
            return max(head, ears);
        }

        // Jaw: below the lips, across the chin, the glass drops.
        float jaw(vec2 p)
        {
            float below = smoothstep(mouthLine(p.x), mouthLine(p.x) + MOUTH_GAP, p.y) * (1.0 - smoothstep(0.94, 0.98, p.y));
            float across = 1.0 - smoothstep(0.15, 0.22, abs(p.x - MOUTH_X));
            return below * across;
        }

        """);

    // Hagane, an android woman, face front (a photo): face axis at x 0.495, from the hairline (y 0.17) to the chin at
    // 0.607, x 0.31 to 0.68; eyes centred at (0.406, 0.361) and (0.572, 0.359) (half size 0.042 × 0.015), their irises of
    // radius 0.013; brows at 0.31; lips slightly parted along y 0.5234, x 0.444 to 0.545; black hair from the top of the
    // frame, machinery on both sides of the head (x 0.02 to 0.3 and 0.7 to 0.98, y 0.04 to 0.6), a robot neck with red
    // wires from 0.6, a white armour on the shoulders from 0.75, cut by the bottom of the frame.
    public static readonly FaceModel HaganeFace = new("avares://KAONOMI.Chat/Assets/hagane-face.png", """
        #define HAGANE
        const vec2 EYE_LEFT = vec2(0.4058, 0.3613);
        const vec2 EYE_RIGHT = vec2(0.5718, 0.3594);
        const vec2 EYE_HALF = vec2(0.042, 0.015);
        const vec2 EYE_SIZE = vec2(0.052, 0.024);   // half size of the eyelid, past the corners of the eye
        const float MOUTH_X = 0.4946;
        const float MOUTH_HALF = 0.05;
        const float MOUTH_GAP = 0.008;
        const float JAW_DROP = 0.04;
        const vec3 MOUTH_DARK = vec3(0.16, 0.05, 0.06);
        const vec3 MOUTH_LIGHT = vec3(0.65, 0.32, 0.34);   // the tongue

        """, """
        float mouthLine(float x)
        {
            return 0.5234;
        }

        float relief(vec2 p)
        {
            float ax = abs(p.x - 0.495);
            float face = 0.34 * sqrt(oval(p, vec2(0.495, 0.4), vec2(0.19, 0.24)));
            face += 0.05 * bump(p, vec2(0.495, 0.45), vec2(0.03, 0.045));                // nose
            face += 0.02 * bump(p, vec2(0.495, 0.39), vec2(0.02, 0.05));                 // nose bridge
            face -= 0.02 * bump(vec2(ax, p.y), vec2(0.083, 0.36), vec2(0.045, 0.02));    // eye sockets
            face += 0.03 * bump(vec2(ax, p.y), vec2(0.11, 0.44), vec2(0.06, 0.05));      // cheeks
            face += 0.02 * bump(p, vec2(0.495, 0.58), vec2(0.06, 0.03));                 // chin
            float hair = 0.26 * sqrt(oval(p, vec2(0.5, 0.25), vec2(0.22, 0.22)));
            float machinery = 0.2 * bump(vec2(ax, p.y), vec2(0.3, 0.33), vec2(0.15, 0.2));
            float neck = 0.15 * sqrt(oval(p, vec2(0.5, 0.68), vec2(0.1, 0.1)));
            float armour = 0.2 * sqrt(oval(p, vec2(0.5, 1.05), vec2(0.5, 0.3)));
            return max(max(face, hair), max(machinery, max(neck, armour)));
        }

        // Jaw: below the parted lips, across the chin, the face drops; the robot neck under the chin takes up the drop.
        float jaw(vec2 p)
        {
            float below = smoothstep(mouthLine(p.x), mouthLine(p.x) + MOUTH_GAP, p.y) * (1.0 - smoothstep(0.6, 0.64, p.y));
            float across = 1.0 - smoothstep(0.1, 0.16, abs(p.x - MOUTH_X));
            return below * across;
        }

        """);

    // Sora, an android girl of an animated film, her head tilted about 7° (rising to the right of the screen) and turned
    // a little to the left: face from the hairline (y 0.2) to the chin at (0.462, 0.676), x 0.27 to 0.66; eyes (fitted
    // ellipses of their white, iris and pupil) centred at (0.333, 0.418), half size 0.049 × 0.032, level, and at
    // (0.548, 0.391), half size 0.055 × 0.034, tilted 20.5° (its outer corner higher), teal irises of radius 0.031
    // around the pupils at (0.347, 0.411) and (0.548, 0.387); nose tip at (0.415, 0.522); lips
    // parted along y 0.585 at x 0.436, rising to the right (x 0.386 to 0.486); a white earpiece on the right of her head,
    // its dark lens centred at (0.781, 0.388) (half size 0.04 × 0.09); a cyan light at the base of her neck at
    // (0.491, 0.762), a cyan ring on her chest at (0.337, 0.952); short blond hair, a lock hanging on the left of her face
    // down to the shoulder, a white suit from 0.78, cut by the bottom of the frame.
    public static readonly FaceModel SoraFace = new("avares://KAONOMI.Chat/Assets/sora-face.png", """
        #define SORA
        const vec2 EYE_LEFT = vec2(0.333, 0.418);
        const vec2 EYE_RIGHT = vec2(0.5479, 0.3906);
        const vec2 LID_LEFT = vec2(0.057, 0.0395);   // half size of the eyelids, past the corners of the eyes
        const vec2 LID_RIGHT = vec2(0.065, 0.0415);
        const float TILT_LEFT = 0.045;               // angle of the long axis of each eye (radians, y down)
        const float TILT_RIGHT = -0.358;
        const vec2 IRIS_LEFT = vec2(0.3467, 0.4114);
        const vec2 IRIS_RIGHT = vec2(0.5483, 0.3869);
        const float IRIS_R = 0.031;
        const vec2 LENS = vec2(0.7813, 0.3882);
        const vec2 LENS_HALF = vec2(0.04, 0.09);
        const vec2 NECK_LIGHT = vec2(0.4907, 0.7617);
        const vec2 BADGE = vec2(0.337, 0.952);
        const float MOUTH_X = 0.436;
        const float MOUTH_HALF = 0.048;
        const float MOUTH_GAP = 0.009;
        const float JAW_DROP = 0.045;
        const vec3 MOUTH_DARK = vec3(0.26, 0.08, 0.09);
        const vec3 MOUTH_LIGHT = vec3(0.85, 0.47, 0.48);   // the tongue

        """, """
        // The parting of the lips, rising to the right with the tilt of her head.
        float mouthLine(float x)
        {
            return 0.583 - 0.1 * (x - MOUTH_X);
        }

        // A point seen from an eye: along its long axis (x) and from its middle towards its lids (y), the axis turned by
        // tilt, in half sizes of its eyelid.
        vec2 inEye(vec2 p, vec2 centre, vec2 size, float tilt)
        {
            vec2 d = p - centre;
            float c = cos(tilt);
            float s = sin(tilt);
            return vec2(c * d.x + s * d.y, c * d.y - s * d.x) / size;
        }

        // How far a point of an eye is from its middle, 1 at the edge of its eyelid: a rounded rectangle more than an
        // ellipse, so that the corners of the eye are covered too.
        float eyeReach(vec2 e)
        {
            return pow(abs(e.x), 2.6) + pow(abs(e.y), 2.6);
        }

        float relief(vec2 p)
        {
            float face = 0.34 * sqrt(oval(p, vec2(0.46, 0.44), vec2(0.2, 0.26)));
            face += 0.05 * bump(p, vec2(0.415, 0.515), vec2(0.03, 0.04));                                  // nose
            face += 0.02 * bump(p, vec2(0.43, 0.45), vec2(0.02, 0.06));                                    // nose bridge
            face -= 0.02 * (bump(p, EYE_LEFT, vec2(0.05, 0.035)) + bump(p, EYE_RIGHT, vec2(0.06, 0.04)));   // eye sockets
            face += 0.03 * (bump(p, vec2(0.33, 0.51), vec2(0.06, 0.05)) + bump(p, vec2(0.58, 0.49), vec2(0.06, 0.05)));   // cheeks
            face += 0.02 * bump(p, vec2(0.462, 0.65), vec2(0.05, 0.03));                                   // chin
            float hair = 0.22 * sqrt(oval(p, vec2(0.48, 0.4), vec2(0.32, 0.4)));
            float hanging = 0.16 * bump(p, vec2(0.27, 0.66), vec2(0.06, 0.17));                            // the lock on the left
            float earpiece = 0.26 * bump(p, vec2(0.765, 0.39), vec2(0.07, 0.15));
            float neck = 0.12 * sqrt(oval(p, vec2(0.5, 0.75), vec2(0.09, 0.1)));
            float suit = 0.2 * sqrt(oval(p, vec2(0.56, 1.12), vec2(0.45, 0.33)));
            return max(max(face, hair), max(max(hanging, earpiece), max(neck, suit)));
        }

        // Jaw: below the lips, across the chin, the face drops; the robot neck under the chin takes up the drop.
        float jaw(vec2 p)
        {
            float below = smoothstep(mouthLine(p.x), mouthLine(p.x) + MOUTH_GAP, p.y) * (1.0 - smoothstep(0.67, 0.71, p.y));
            float across = 1.0 - smoothstep(0.1, 0.16, abs(p.x - 0.455));
            return below * across;
        }

        """);

    // Tama, a white robot with a round head, face front (a render): head from y 0.055 to the chin at 0.65, x 0.223 to
    // 0.781, its ears on the sides at (0.21, 0.407) and (0.791, 0.407); camera eyes, their dark lenses centred at
    // (0.341, 0.36) and (0.66, 0.36) (radius 0.08 and 0.073), glowing orange cores at (0.347, 0.359) and (0.656, 0.359)
    // (radius 0.04 and 0.037) inside a broken blue ring (at about 0.6 to 0.75 of the lens); no jaw: a grille for a mouth
    // centred at (0.502, 0.555) (half size 0.057 × 0.023), two rows of dark blue cells parted by a bar at y 0.553; two
    // small lights on the chest at (0.386, 0.906) and (0.625, 0.905); a robot neck from 0.65, the shoulders and the arms
    // cut by the bottom and the sides of the frame.
    public static readonly FaceModel TamaFace = new("avares://KAONOMI.Chat/Assets/tama-face.png", """
        #define TAMA
        const vec2 LENS_LEFT = vec2(0.3408, 0.3604);
        const vec2 LENS_RIGHT = vec2(0.6602, 0.3604);
        const float LENS_R_LEFT = 0.08;
        const float LENS_R_RIGHT = 0.073;
        const vec2 CORE_LEFT = vec2(0.3467, 0.3594);
        const vec2 CORE_RIGHT = vec2(0.6563, 0.3594);
        // The dark bands of each lens (shares of its radius) inside and outside its blue ring: where the turning ring
        // and the gliding core blend into the still lens.
        const vec4 DARK_LEFT = vec4(0.535, 0.565, 0.885, 0.925);
        const vec4 DARK_RIGHT = vec4(0.455, 0.49, 0.945, 0.985);
        const vec2 GRILLE = vec2(0.502, 0.5552);
        const vec2 GRILLE_HALF = vec2(0.0566, 0.023);
        const float GRILLE_BAR = 0.5527;      // y of the bar between its two rows of cells
        const float GRILLE_COLUMN = 0.0205;   // width of a column of cells, from the bar at x 0.5005
        const vec2 CHEST_LEFT = vec2(0.386, 0.9059);
        const vec2 CHEST_RIGHT = vec2(0.6249, 0.9049);
        // No jaw: the grille of the mouth lights up with the voice instead (the mouth never opens).
        const float MOUTH_X = 0.502;
        const float MOUTH_HALF = 0.0;
        const float MOUTH_GAP = 1.0;
        const float JAW_DROP = 0.0;
        const vec3 MOUTH_DARK = vec3(0.0);
        const vec3 MOUTH_LIGHT = vec3(0.0);

        """, """
        float mouthLine(float x)
        {
            return -10.0;
        }

        float relief(vec2 p)
        {
            float ax = abs(p.x - 0.502);
            float head = 0.38 * sqrt(oval(p, vec2(0.502, 0.352), vec2(0.28, 0.298)));
            head -= 0.03 * (bump(p, LENS_LEFT, vec2(0.085, 0.085)) + bump(p, LENS_RIGHT, vec2(0.08, 0.08)));   // eye sockets
            head += 0.02 * (bump(p, CORE_LEFT, vec2(0.035, 0.035)) + bump(p, CORE_RIGHT, vec2(0.033, 0.033)));   // core glass
            head -= 0.01 * bump(p, GRILLE, GRILLE_HALF);
            float ears = 0.2 * bump(vec2(ax, p.y), vec2(0.29, 0.407), vec2(0.035, 0.09));
            float neck = 0.12 * sqrt(oval(p, vec2(0.5, 0.68), vec2(0.08, 0.07)));
            float body = 0.22 * sqrt(oval(p, vec2(0.5, 1.15), vec2(0.36, 0.42)));
            float shoulders = 0.18 * bump(vec2(ax, p.y), vec2(0.36, 0.88), vec2(0.08, 0.12));
            return max(max(head, ears), max(neck, max(body, shoulders)));
        }

        float jaw(vec2 p)
        {
            return 0.0;
        }

        """);

    // Beni, a small red and white robot, its whole body (a render), turned a little to the right of the screen and
    // waving: a round red helmet from y 0.03 to 0.49, x 0.26 to 0.78, its ears at x 0.25 and 0.83 (y 0.3); a white
    // screen for a face, its eyes drawn on it: dark ellipses in a light grey rim, the rims centred at (0.403, 0.322) and
    // (0.668, 0.322) (half size 0.0625 × 0.073 and 0.06 × 0.073), a blue glint low in each at (0.403, 0.347) and
    // (0.668, 0.348); a thin drawn smile from (0.562, 0.419) to (0.63, 0.407), lowest at x 0.586 (y 0.423); a round light
    // on its chest at (0.524, 0.627) (radius 0.044); its right hand raised, a fist above the wrist at (0.889, 0.635);
    // its legs cut by the bottom of the frame.
    public static readonly FaceModel BeniFace = new("avares://KAONOMI.Chat/Assets/beni-face.png", """
        #define BENI
        const vec2 EYE_LEFT = vec2(0.4033, 0.3223);
        const vec2 EYE_RIGHT = vec2(0.6675, 0.3223);
        const vec2 RIM_LEFT = vec2(0.0625, 0.0732);
        const vec2 RIM_RIGHT = vec2(0.06, 0.0732);
        const vec2 GLINT_LEFT = vec2(0.4028, 0.3467);
        const vec2 GLINT_RIGHT = vec2(0.668, 0.3477);
        const float SMILE_X = 0.586;      // lowest point of the drawn smile
        const float SMILE_Y = 0.4229;
        const float SMILE_BEND = 7.4;     // its rise towards its ends
        const vec2 TALK = vec2(0.5957, 0.034);   // middle and half width of the mouth that opens under the smile
        const vec2 CHEST = vec2(0.5239, 0.6268);
        const float CHEST_R = 0.044;
        const vec2 WRIST = vec2(0.889, 0.635);
        // No jaw: the mouth is drawn on the screen of its face, under the smile.
        const float MOUTH_X = 0.5957;
        const float MOUTH_HALF = 0.0;
        const float MOUTH_GAP = 1.0;
        const float JAW_DROP = 0.0;
        const vec3 MOUTH_DARK = vec3(0.0);
        const vec3 MOUTH_LIGHT = vec3(0.0);

        """, """
        float mouthLine(float x)
        {
            return -10.0;
        }

        float relief(vec2 p)
        {
            float ax = abs(p.x - 0.54);
            float helmet = 0.36 * sqrt(oval(p, vec2(0.52, 0.27), vec2(0.27, 0.235)));
            helmet -= 0.02 * bump(p, vec2(0.535, 0.33), vec2(0.2, 0.1));                                   // the screen
            float ears = 0.22 * bump(vec2(ax, p.y), vec2(0.29, 0.3), vec2(0.04, 0.1));
            float body = 0.26 * sqrt(oval(p, vec2(0.53, 0.72), vec2(0.22, 0.2)));
            float raised = 0.22 * bump(p, vec2(0.86, 0.62), vec2(0.09, 0.14));                               // right arm
            float hanging = 0.2 * bump(p, vec2(0.17, 0.72), vec2(0.1, 0.2));                                 // left arm
            float legs = 0.2 * bump(vec2(ax, p.y), vec2(0.13, 0.95), vec2(0.1, 0.1));
            return max(max(helmet, ears), max(max(body, legs), max(raised, hanging)));
        }

        float jaw(vec2 p)
        {
            return 0.0;
        }

        """);

    // Helpers of the shapes of the faces.
    public const string ShapeFunctions = """
        float bump(vec2 p, vec2 center, vec2 size)
        {
            vec2 d = (p - center) / size;
            return exp(-dot(d, d));
        }

        float oval(vec2 p, vec2 center, vec2 radius)
        {
            vec2 d = (p - center) / radius;
            return max(0.0, 1.0 - dot(d, d));
        }

        // Where the eyes look for the glance n (RIVET, KINGYO): -1 to 1 across, less up and down; about one glance in
        // three straight ahead.
        vec2 gaze(float n)
        {
            n = mod(n, 97.0);
            float a = fract(sin(n * 12.9898) * 43758.5453);
            float b = fract(sin(n * 78.233) * 43758.5453);
            float c = fract(sin(n * 39.425) * 43758.5453);
            return c < 0.35 ? vec2(0.0) : vec2(a - 0.5, (b - 0.5) * 0.7) * 2.0;
        }

        """;

    public const string VertexShader = """
        in vec2 aUv;
        uniform mat4 uMvp;
        uniform float uOpen;
        out vec2 vUv;
        out float vLight;

        void main()
        {
            vUv = aUv;
            float z = relief(aUv);
            float e = 0.004;
            vec3 normal = normalize(vec3(-(relief(aUv + vec2(e, 0.0)) - z) / e * 0.5, (relief(aUv + vec2(0.0, e)) - z) / e * 0.5, 1.0));
            vLight = 0.7 + 0.3 * dot(normal, normalize(vec3(-0.4, 0.4, 0.8)));

            vec2 position = vec2(aUv.x * 2.0 - 1.0, 1.0 - aUv.y * 2.0);
            position.y -= uOpen * JAW_DROP * jaw(aUv);
            gl_Position = uMvp * vec4(position, z, 1.0);
        }
        """;

    public const string FragmentShader = """
        in vec2 vUv;
        in float vLight;
        uniform sampler2D uFace;
        uniform float uTime;
        uniform float uOpen;
        uniform vec3 uGlitch;
        out vec4 fragColor;

        void main()
        {
            vec2 uv = vUv;
        #if defined(HOLOGRAM) || defined(KILLER) || defined(KAMEN) || defined(AOI)
            // Glitch: a thin band of the image slides sideways.
            if (abs(uv.y - uGlitch.x) < 0.012)
                uv.x += uGlitch.y;
        #endif
        #ifdef MUSUKO
            // A blink every few seconds: the eyelids come down over the eyes (the skin just under each eye, darker,
            // with the line of the lashes at their edge).
            float blinkPhase = fract(uTime / 4.3);
            float blink = blinkPhase < 0.045 ? sin(blinkPhase / 0.045 * 3.14159) : 0.0;
            vec2 eye = uv.x < 0.483 ? EYE_LEFT : EYE_RIGHT;
            vec2 e = (uv - eye) / vec2(0.05, 0.024);
            float lid = -1.0 + 2.0 * blink;   // edge of the eyelid, from above the eye (-1) to below it (1)
            // Share of the eyelid over this point: inside the eye, above the edge of the lid (soft edges).
            float covered = blink > 0.0 ? (1.0 - smoothstep(0.75, 1.05, dot(e, e))) * (1.0 - smoothstep(lid - 0.12, lid, e.y)) : 0.0;
        #endif
        #ifdef KILLER
            // The spiral pupils spin, faster while she speaks.
            float turn = uTime * 2.2 + uOpen * 3.0;
            uv = spin(spin(uv, EYE_LEFT, turn), EYE_RIGHT, turn);
        #endif
        #ifdef GREEN
            // The pupils dilate with the voice and breathe slowly when it is quiet (the inside of each eye is magnified
            // around its centre).
            vec2 eye = uv.x < 0.505 ? EYE_LEFT : EYE_RIGHT;
            vec2 fromEye = uv - eye;
            float inEye = 1.0 - smoothstep(0.05, 0.1, length(fromEye / vec2(1.0, 0.55)));
            uv = eye + fromEye * (1.0 - (0.12 * uOpen + 0.03 * sin(uTime * 1.5)) * inEye);
            // A blink every few seconds: the eyelids come down over the eyes.
            float blinkPhase = fract(uTime / 5.1);
            float blink = blinkPhase < 0.05 ? sin(blinkPhase / 0.05 * 3.14159) : 0.0;
            vec2 e = (vUv - eye) / vec2(0.11, 0.05);
            float lid = -1.0 + 2.0 * blink;   // edge of the eyelid, from above the eye (-1) to below it (1)
            float covered = blink > 0.0 ? (1.0 - smoothstep(0.75, 1.05, dot(e, e))) * (1.0 - smoothstep(lid - 0.12, lid, e.y)) : 0.0;
        #endif
        #ifdef HAL
            // The iris dilates with the voice and breathes slowly when it is quiet.
            vec2 fromEye = uv - EYE;
            float dilate = (0.10 * uOpen + 0.02 * sin(uTime * 1.3)) * (1.0 - smoothstep(0.12, 0.22, length(fromEye)));
            uv = EYE + fromEye * (1.0 - dilate);
        #endif
        #ifdef RIVET
            // The eyes dart to another place every 2.6 s (both the same way, in a tenth of a second), and the pupils widen
            // with the voice and breathe when it is quiet: inside each eyeball the picture slides, then is magnified
            // around the pupil, the white rim staying where it is.
            vec2 eye = uv.x < 0.447 ? EYE_LEFT : EYE_RIGHT;
            float glance = uTime / 2.6;
            vec2 look = mix(gaze(floor(glance) - 1.0), gaze(floor(glance)), smoothstep(0.0, 0.04, fract(glance)));
            uv -= look * 0.016 * (1.0 - smoothstep(0.5, 0.95, length(uv - eye) / EYE_R));
            vec2 fromPupil = uv - eye;
            float inPupil = 1.0 - smoothstep(0.03, 0.05, length(fromPupil));
            uv = eye + fromPupil * (1.0 - (0.2 * uOpen + 0.04 * sin(uTime * 1.7)) * inPupil);
            // A blink every few seconds: a steel shutter comes down over each eyeball, cut from the metal of the head
            // above the eye. Sampled outside any branch: inside one, its edge would pick a wrong mipmap.
            float blinkPhase = fract(uTime / 4.9);
            float blink = blinkPhase < 0.05 ? sin(blinkPhase / 0.05 * 3.14159) : 0.0;
            vec2 e = (vUv - eye) / EYE_R;
            float lid = -1.15 + 2.3 * blink;   // edge of the shutter, from above the eyeball (-1.15) to below it (1.15)
            // A little wider than the eyeball (EYE_R is a mean of both): no white is left under it when it is closed.
            float covered = blink > 0.0 ? (1.0 - smoothstep(0.98, 1.1, length(e))) * (1.0 - smoothstep(lid - 0.06, lid, e.y)) : 0.0;
            vec4 shutter = texture(uFace, vUv - vec2(0.0, 0.17));
        #endif
        #ifdef SHIRO
            // The pupils widen with the voice and breathe slowly when he is quiet: the inside of each iris is magnified
            // around its centre.
            vec2 iris = uv.x < 0.5 ? IRIS_LEFT : IRIS_RIGHT;
            vec2 fromIris = uv - iris;
            float inIris = 1.0 - smoothstep(IRIS_R * 0.7, IRIS_R * 1.25, length(fromIris));
            uv = iris + fromIris * (1.0 - (0.14 * uOpen + 0.03 * sin(uTime * 1.5)) * inIris);
            // A blink every few seconds: the eyelids, of the grey of his cheeks, come down over the eyes, the dark line of
            // the lashes at their edge. The cheek is sampled outside any branch (inside one, a wrong mipmap at the edge).
            float blinkPhase = fract(uTime / 4.4);
            float blink = blinkPhase < 0.05 ? sin(blinkPhase / 0.05 * 3.14159) : 0.0;
            vec2 eye = vUv.x < 0.5 ? EYE_LEFT : EYE_RIGHT;
            vec2 e = (vUv - eye) / EYE_SIZE;
            float lid = -1.1 + 1.75 * blink;   // edge of the eyelid, from above the eye (-1.1) to its lower half (0.65)
            float eyeArea = 1.0 - smoothstep(0.55, 1.0, dot(e, e));
            float covered = blink > 0.0 ? eyeArea * (1.0 - smoothstep(lid - 0.2, lid, e.y)) : 0.0;
            vec4 cheek = texture(uFace, vec2(vUv.x, eye.y + 0.065));
        #endif
        #ifdef KINGYO
            // The fins move, the picture drawn from a little aside in their places, the body staying still: the tail beats
            // (its width breathes, a wave runs along its rays), the dorsal fin and the fins under the belly flutter.
            float tail = smoothstep(0.735, 0.78, uv.x);
            float fromRoot = uv.x - 0.75;
            uv.x = mix(uv.x, 0.75 + fromRoot * (1.0 + 0.07 * sin(uTime * 3.1)), tail);
            uv.y += tail * 0.012 * sin(uTime * 3.1 - fromRoot * 20.0) * smoothstep(0.0, 0.15, fromRoot);
            float dorsal = smoothstep(0.0, 0.07, 0.315 - uv.y) * smoothstep(0.33, 0.37, uv.x) * (1.0 - smoothstep(0.58, 0.62, uv.x));
            uv.x += dorsal * 0.01 * sin(uTime * 2.6 - (0.315 - uv.y) * 22.0);
            float belly = smoothstep(0.0, 0.08, uv.y - 0.665) * smoothstep(0.35, 0.39, uv.x) * (1.0 - smoothstep(0.76, 0.8, uv.x));
            uv.x += belly * 0.012 * sin(uTime * 2.2 - (uv.y - 0.665) * 16.0);
            // The eye looks elsewhere every 2.9 s and its pupil widens with the voice (the picture slides, then is
            // magnified, inside the eyeball only).
            float glance = uTime / 2.9;
            vec2 look = mix(gaze(floor(glance) - 1.0), gaze(floor(glance)), smoothstep(0.0, 0.05, fract(glance)));
            uv -= look * 0.006 * (1.0 - smoothstep(0.6, 0.98, length(uv - EYE) / EYE_R));
            vec2 fromPupil = uv - EYE;
            float inPupil = 1.0 - smoothstep(0.016, 0.026, length(fromPupil));
            uv = EYE + fromPupil * (1.0 - (0.22 * uOpen + 0.04 * sin(uTime * 1.6)) * inPupil);
            // A blink every few seconds: a shutter of the orange metal above the eye comes down over it (sampled
            // outside any branch: inside one, its edge would pick a wrong mipmap).
            float blinkPhase = fract(uTime / 5.3);
            float blink = blinkPhase < 0.05 ? sin(blinkPhase / 0.05 * 3.14159) : 0.0;
            vec2 e = (vUv - EYE) / EYE_R;
            float lid = -1.15 + 2.3 * blink;
            float covered = blink > 0.0 ? (1.0 - smoothstep(0.98, 1.1, length(e))) * (1.0 - smoothstep(lid - 0.06, lid, e.y)) : 0.0;
            vec4 shutter = texture(uFace, vUv - vec2(0.0, 0.075));
            // The end cap of the motor turns: its ellipse is made a circle, turned, and made an ellipse again.
            vec2 cap = (uv - CAP) / CAP_R;
            float turn = uTime * 2.2 * (1.0 - smoothstep(0.85, 1.0, length(cap)));
            uv = CAP + (mat2(cos(turn), -sin(turn), sin(turn), cos(turn)) * cap) * CAP_R;
        #endif
        #ifdef HIKARI
            // Her hair sways in a light breeze on both sides of her face, its ends the most (not where it lies over the
            // face, nor the armour).
            float leftHair = (1.0 - smoothstep(0.27, 0.31, uv.x)) * smoothstep(0.45, 0.55, uv.y);
            float rightHair = smoothstep(0.72, 0.76, uv.x) * smoothstep(0.47, 0.57, uv.y);
            float armour = smoothstep(0.08, 0.12, uv.x) * (1.0 - smoothstep(0.88, 0.92, uv.x)) * smoothstep(0.74, 0.79, uv.y);
            uv.x += (leftHair + rightHair) * (1.0 - armour) * smoothstep(0.45, 0.85, uv.y) * 0.007 * sin(uTime * 1.3 - uv.y * 7.0);
            // The pupils widen with the voice and breathe slowly when she is quiet (the inside of each iris magnified).
            vec2 iris = uv.x < 0.52 ? IRIS_LEFT : IRIS_RIGHT;
            vec2 fromIris = uv - iris;
            float inIris = 1.0 - smoothstep(IRIS_R * 0.55, IRIS_R * 0.95, length(fromIris));
            uv = iris + fromIris * (1.0 - (0.15 * uOpen + 0.03 * sin(uTime * 1.5)) * inIris);
            // A blink every few seconds: the eyelid comes down to the lower lashes, its edge carrying the line of the upper
            // lashes; its skin goes from the one between the brow and the lashes to the one of the cheek, read blurred (a
            // small mipmap: a row of the photo, with its freckles, would streak it).
            float blinkPhase = fract(uTime / 4.6);
            float blink = blinkPhase < 0.05 ? sin(blinkPhase / 0.05 * 3.14159) : 0.0;
            vec2 eye = vUv.x < 0.52 ? EYE_LEFT : EYE_RIGHT;
            vec2 e = (vUv - eye) / EYE_SIZE;
            float lid = -1.05 + 2.0 * blink;   // edge of the eyelid, from above the eye (-1.05) to its lower lashes (0.95)
            float eyeArea = 1.0 - smoothstep(0.7, 1.0, dot(e, e));
            float covered = blink > 0.0 ? eyeArea * (1.0 - smoothstep(lid - 0.2, lid, e.y)) : 0.0;
            vec4 skin = mix(textureLod(uFace, vec2(vUv.x, 0.318), 3.5), textureLod(uFace, vec2(vUv.x, eye.y + 0.075), 3.5),
                            smoothstep(-1.0, 1.0, e.y));
        #endif
        #ifdef HAGANE
            // Her eyes glance aside every 3.1 s, both the same way (the picture slides inside the openings of the eyes,
            // their corners staying where they are).
            vec2 eye = vUv.x < 0.49 ? EYE_LEFT : EYE_RIGHT;
            float glance = uTime / 3.1;
            vec2 look = mix(gaze(floor(glance) - 1.0), gaze(floor(glance)), smoothstep(0.0, 0.06, fract(glance)));
            uv -= look * vec2(0.007, 0.002) * (1.0 - smoothstep(0.45, 0.95, length((uv - eye) / EYE_HALF)));
            // A blink every few seconds: the eyelid comes down to the lower lashes, its skin going from the one under the
            // brow to the one under the eye, read blurred; its edge carries the line of the lashes.
            float blinkPhase = fract(uTime / 4.8);
            float blink = blinkPhase < 0.05 ? sin(blinkPhase / 0.05 * 3.14159) : 0.0;
            vec2 e = (vUv - eye) / EYE_SIZE;
            float lid = -1.05 + 1.9 * blink;
            float eyeArea = 1.0 - smoothstep(0.65, 1.0, dot(e, e));
            float covered = blink > 0.0 ? eyeArea * (1.0 - smoothstep(lid - 0.2, lid, e.y)) : 0.0;
            vec4 skin = mix(textureLod(uFace, vec2(vUv.x, eye.y - 0.031), 3.0), textureLod(uFace, vec2(vUv.x, eye.y + 0.045), 3.0),
                            smoothstep(-1.0, 1.0, e.y));
        #endif
        #ifdef SORA
            // Her hair sways in a light breeze: the lock hanging on the left of her face (its end the most) and the strand
            // under her earpiece; not her face, her neck nor her suit.
            float faceEdge = mix(0.29, 0.335, smoothstep(0.62, 0.68, uv.y));
            float leftLock = (1.0 - smoothstep(faceEdge - 0.02, faceEdge + 0.005, uv.x)) * smoothstep(0.42, 0.52, uv.y) * (1.0 - smoothstep(0.8, 0.86, uv.y));
            float strand = smoothstep(0.665, 0.685, uv.x) * (1.0 - smoothstep(0.75, 0.78, uv.x)) * smoothstep(0.54, 0.58, uv.y) * (1.0 - smoothstep(0.7, 0.74, uv.y));
            uv.x += (leftLock * smoothstep(0.42, 0.82, uv.y) + strand * smoothstep(0.54, 0.72, uv.y)) * 0.008 * sin(uTime * 1.2 - uv.y * 7.0);
            // Her eyes glance aside every 3.3 s, both the same way, and her pupils widen with the voice and breathe when
            // she is quiet: the picture slides, then is magnified, inside the openings of the eyes (their lids staying).
            bool leftEye = vUv.x < 0.443;
            vec2 eye = leftEye ? EYE_LEFT : EYE_RIGHT;
            vec2 lidSize = leftEye ? LID_LEFT : LID_RIGHT;
            float tilt = leftEye ? TILT_LEFT : TILT_RIGHT;
            float glance = uTime / 3.3;
            vec2 look = mix(gaze(floor(glance) - 1.0), gaze(floor(glance)), smoothstep(0.0, 0.06, fract(glance)));
            vec2 q = inEye(uv, eye, lidSize, tilt);
            uv -= look * vec2(0.008, 0.003) * (1.0 - smoothstep(0.3, 0.7, dot(q, q)));
            vec2 iris = leftEye ? IRIS_LEFT : IRIS_RIGHT;
            vec2 fromIris = uv - iris;
            float inIris = 1.0 - smoothstep(IRIS_R * 0.5, IRIS_R * 0.9, length(fromIris));
            uv = iris + fromIris * (1.0 - (0.15 * uOpen + 0.03 * sin(uTime * 1.5)) * inIris);
            // A blink every few seconds: the eyelid comes down to the lower lashes across the axis of the eye, its edge
            // curved (its middle lower than its corners); its skin goes from the one under the brow to the one of the
            // cheek, read blurred. The line of the lashes runs along its edge, and stays across the lower part of the
            // closed eye.
            float blinkPhase = fract(uTime / 4.4);
            float blink = blinkPhase < 0.05 ? sin(blinkPhase / 0.05 * 3.14159) : 0.0;
            vec2 e = inEye(vUv, eye, lidSize, tilt);
            float lid = -1.1 + 2.4 * blink;   // middle of the edge of the eyelid, from above the eye (-1.1) to below it (1.3)
            float edge = lid - 0.3 * e.x * e.x;
            float eyeArea = 1.0 - smoothstep(0.72, 1.12, eyeReach(e));
            float covered = blink > 0.0 ? eyeArea * (1.0 - smoothstep(edge - 0.15, edge, e.y)) : 0.0;
            float lashLine = min(lid - 0.12, 0.72) - 0.3 * e.x * e.x;
            float rise = eye.y + tan(tilt) * (vUv.x - eye.x);
            vec4 skin = mix(textureLod(uFace, vec2(vUv.x, rise - 0.06), 3.0), textureLod(uFace, vec2(vUv.x, rise + 0.06), 3.0),
                            smoothstep(-1.0, 1.0, e.y));
        #endif
        #ifdef TAMA
            // Its cores look elsewhere every 2.8 s, both the same way (each core slides inside its dark lens), and widen
            // with the voice and breathe when it is quiet; the broken blue ring around each core turns slowly, faster as
            // it speaks. Both blend into the still lens in its dark bands, where the warp does not show.
            bool leftEye = vUv.x < 0.502;
            vec2 eyeLens = leftEye ? LENS_LEFT : LENS_RIGHT;
            float lensR = leftEye ? LENS_R_LEFT : LENS_R_RIGHT;
            vec2 core = leftEye ? CORE_LEFT : CORE_RIGHT;
            vec4 dark = leftEye ? DARK_LEFT : DARK_RIGHT;
            float glance = uTime / 2.8;
            vec2 look = mix(gaze(floor(glance) - 1.0), gaze(floor(glance)), smoothstep(0.0, 0.05, fract(glance)));
            uv -= look * lensR * 0.06 * (1.0 - smoothstep(dark.x - 0.06, dark.y, length(uv - eyeLens) / lensR));
            float rr = length(uv - eyeLens) / lensR;
            float turn = (uTime * 0.5 + uOpen * 1.5) * smoothstep(dark.x, dark.y, rr) * (1.0 - smoothstep(dark.z, dark.w, rr));
            uv = eyeLens + mat2(cos(turn), -sin(turn), sin(turn), cos(turn)) * (uv - eyeLens);
            vec2 fromCore = uv - core;
            float inCore = 1.0 - smoothstep(0.35, 0.5, length(fromCore) / lensR);
            uv = core + fromCore * (1.0 - (0.2 * uOpen + 0.04 * sin(uTime * 1.6)) * inCore);
        #endif
        #ifdef BENI
            // Its raised hand waves now and then: every 8 s, for about two seconds, the hand turns to and fro around its
            // wrist (the picture turned above the wrist, the arm and the shoulder staying where they are).
            float waving = sin(3.14159 * clamp(fract(uTime / 8.0) / 0.3, 0.0, 1.0));
            float hand = smoothstep(0.815, 0.835, uv.x) * (1.0 - smoothstep(WRIST.y - 0.025, WRIST.y + 0.005, uv.y));
            float wave = waving * 0.18 * sin(uTime * 8.0) * hand;
            uv = WRIST + mat2(cos(wave), -sin(wave), sin(wave), cos(wave)) * (uv - WRIST);
            // Its eyes, drawn on the screen of its face, look elsewhere every 3 s and close to a line to blink: inside the
            // area of each eye the picture of the eye is moved and squashed, on the white of the screen read beside it.
            bool leftEye = vUv.x < 0.535;
            vec2 eye = leftEye ? EYE_LEFT : EYE_RIGHT;
            vec2 rim = leftEye ? RIM_LEFT : RIM_RIGHT;
            float glance = uTime / 3.0;
            vec2 look = mix(gaze(floor(glance) - 1.0), gaze(floor(glance)), smoothstep(0.0, 0.06, fract(glance))) * vec2(0.008, 0.005);
            float blinkPhase = fract(uTime / 4.6);
            float blink = blinkPhase < 0.05 ? sin(blinkPhase / 0.05 * 3.14159) : 0.0;
            float squash = 1.0 - 0.92 * blink;
            vec2 q = (vUv - eye - look) / vec2(1.0, squash);
            vec2 d = (vUv - eye) / rim;
            float eyeArea = 1.0 - smoothstep(1.1, 1.2, length(d));
            float inEye = 1.0 - smoothstep(0.97, 1.02, length(q / rim));
            vec4 drawnEye = textureGrad(uFace, eye + q, dFdx(vUv), dFdy(vUv));
            // The white of the screen where the eye no longer is: read on the edge of the area of the eye, above and
            // below the point and on its left and right, and blended by how near each edge is (the screen is shaded,
            // darker where it curves away), so that it meets the screen around the area.
            float hy = sqrt(max(1.44 - d.x * d.x, 0.0));   // half height of the area at this x, in rims
            float hx = sqrt(max(1.44 - d.y * d.y, 0.0));   // its half width at this y
            vec4 vertical = mix(textureLod(uFace, vec2(vUv.x, eye.y - hy * rim.y), 1.5), textureLod(uFace, vec2(vUv.x, eye.y + hy * rim.y), 1.5),
                                clamp((d.y + hy) / max(2.0 * hy, 0.001), 0.0, 1.0));
            vec4 horizontal = mix(textureLod(uFace, vec2(eye.x - hx * rim.x, vUv.y), 1.5), textureLod(uFace, vec2(eye.x + hx * rim.x, vUv.y), 1.5),
                                  clamp((d.x + hx) / max(2.0 * hx, 0.001), 0.0, 1.0));
            float toTopOrBottom = max(hy - abs(d.y), 0.0);
            float toSide = max(hx - abs(d.x), 0.0);
            vec4 screenWhite = mix(vertical, horizontal, toTopOrBottom / max(toTopOrBottom + toSide, 0.001));
        #endif
        #if defined(TAMA) || defined(BENI)
            // Read at the scale of the picture itself: the turned rings would pick blurred mipmaps, in blocks of pixels.
            vec4 color = textureGrad(uFace, uv, dFdx(vUv), dFdy(vUv));   // premultiplied alpha
        #else
            vec4 color = texture(uFace, uv);   // premultiplied alpha
        #endif
        #ifdef LIVE
            // A blink every few seconds: the eyelids come down over the eyes. An eyelid (EYE_SIZE, a little larger than
            // the eye, so that its corners and lashes are covered too) takes the colour of the skin above and below the
            // eye, its edge soft; when closed, a thin soft line of lashes lies across the lower half of the eye.
            float blinkPhase = fract(uTime / 4.7);
            float blink = blinkPhase < 0.05 ? sin(blinkPhase / 0.05 * 3.14159) : 0.0;
            vec2 eye = uv.x < 0.5 ? EYE_LEFT : EYE_RIGHT;
            vec2 e = (uv - eye) / EYE_SIZE;
            float lid = -1.1 + 1.7 * blink;   // edge of the eyelid, from above the eye (-1.1) to its lower half (0.6)
            float eyeArea = 1.0 - smoothstep(0.4, 1.0, dot(e, e));
            float covered = blink > 0.0 ? eyeArea * (1.0 - smoothstep(lid - 0.3, lid, e.y)) : 0.0;
            // Sampled outside any branch: inside one, the edge of the eyelid would pick a wrong mipmap (a dotted outline).
            // The skin beyond the crease above the eye, and below the shadow under it.
            vec4 above = texture(uFace, vec2(uv.x, eye.y - EYE_SIZE.y * 2.1));
            vec4 below = texture(uFace, vec2(uv.x, eye.y + EYE_SIZE.y * 2.0));
            vec4 skin = mix(above, below, 0.5);
            float lash = smoothstep(0.22, 0.0, abs(e.y - lid)) * smoothstep(0.3, 1.0, blink) * (1.0 - smoothstep(0.55, 0.95, abs(e.x)));
            // The eyelid is rounded: a little lighter at its middle, a little darker towards its edge and the lashes.
            float shade = 0.97 + 0.05 * (1.0 - clamp(dot(e, e), 0.0, 1.0)) - 0.05 * smoothstep(-0.2, lid, e.y);
            vec3 lid3 = mix(skin.rgb * shade, vec3(0.16, 0.11, 0.1) * skin.a, 0.55 * lash);
            color = mix(color, vec4(lid3, skin.a), covered);
        #endif

            // Open mouth: the thin band just below the line of the mouth is stretched when the jaw drops; it shows
            // the inside of the mouth (dark, the tongue lighter at the bottom), shaped like a lens.
            float depth = (uv.y - mouthLine(uv.x)) / MOUTH_GAP;
            float lens = 2.0 * depth - 1.0;
        #ifdef RIVET
            // A box that opens: the gap is as wide at its top as at its bottom.
            float halfWidth = MOUTH_HALF;
        #else
            float halfWidth = MOUTH_HALF * sqrt(max(0.0, 1.0 - lens * lens));
        #endif
            float inside = smoothstep(0.0, 0.08, depth) * (1.0 - smoothstep(0.92, 1.0, depth))
                           * (1.0 - smoothstep(halfWidth - 0.012, halfWidth, abs(uv.x - MOUTH_X)))
                           * smoothstep(0.02, 0.15, uOpen);
            vec3 mouth = mix(MOUTH_DARK, MOUTH_LIGHT, smoothstep(0.45, 0.95, depth));
            color.rgb = mix(color.rgb, mouth * color.a, inside);

        #ifdef LIVE
            // Softened light from the relief (the photo has its own); the shoulders, cut straight by the frame at the
            // bottom and on the sides, fade out instead.
            color.rgb *= mix(1.0, vLight, 0.6);
            color *= (1.0 - smoothstep(0.9, 1.0, vUv.y)) * smoothstep(0.0, 0.08, vUv.x) * (1.0 - smoothstep(0.92, 1.0, vUv.x));
        #ifdef CIRCUIT
            // The green circuits glow with the voice (softly breathing when she is quiet), and a light runs down them.
            float greenness = clamp((color.g - max(color.r, color.b)) * 5.0, 0.0, 1.0);
            float level = 0.12 + 0.1 * (0.5 + 0.5 * sin(uTime * 1.6)) + 0.9 * uOpen;
            float runY = (vUv.y - fract(uTime * 0.3) * 1.3 + 0.15) / 0.04;
            float run = exp(-runY * runY);
            color.rgb += vec3(0.3, 1.0, 0.5) * greenness * (level + 0.8 * run) * color.a;
        #endif
        #ifdef MOTUKO
            // The amber lights of the armour (under the chin only, away from the lips) glow with the voice.
            float amber = clamp((color.r - color.b - 0.3) * 4.0, 0.0, 1.0) * step(0.66, vUv.y);
            float glow = 0.15 + 0.1 * (0.5 + 0.5 * sin(uTime * 2.0)) + 1.2 * uOpen;
            color.rgb += vec3(1.0, 0.6, 0.15) * amber * glow * color.a;
        #endif
        #elif defined(KAONAMI)
            // Softened light from the relief; the shoulders, cut by the frame, fade out at the bottom and on the sides.
            color.rgb *= mix(1.0, vLight, 0.55);
            color *= (1.0 - smoothstep(0.9, 1.0, vUv.y)) * smoothstep(0.02, 0.1, vUv.x) * (1.0 - smoothstep(0.9, 0.98, vUv.x));
            // An amber light in the dark of the eye holes and of the mouth slot: faint when quiet, bright while speaking.
            float dark = 1.0 - smoothstep(0.08, 0.3, max(color.r, max(color.g, color.b)));
            float flicker = 0.85 + 0.15 * sin(uTime * 23.0) * sin(uTime * 7.0);
            float holes = exp(-length(vUv - EYE_LEFT) / 0.016) + exp(-length(vUv - EYE_RIGHT) / 0.016);
            vec2 s = (vUv - SLOT) / vec2(0.06, 0.012);
            float slot = exp(-dot(s, s));
            float shine = (holes + slot) * dark * (0.12 + 1.1 * uOpen * flicker);
            color.rgb += vec3(1.0, 0.55, 0.15) * shine * color.a;
        #elif defined(GREEN)
            if (covered > 0.0)
            {
                // Skin of the eyelid: the pale skin above the eye, darker at the line of the lashes.
                vec4 skin = texture(uFace, vec2(vUv.x, eye.y - 0.075));
                float lash = smoothstep(0.22, 0.0, abs(e.y - lid)) * (1.0 - smoothstep(0.7, 1.0, abs(e.x)));
                vec4 eyelid = vec4(mix(skin.rgb * 0.9, vec3(0.02, 0.03, 0.03) * skin.a, lash), skin.a);
                color = mix(color, eyelid, covered);
            }
            // Softened light from the relief; the head, cut by the top of the frame, and the shoulders, cut at the bottom,
            // fade out.
            color.rgb *= mix(1.0, vLight, 0.5);
            color *= smoothstep(0.083, 0.14, vUv.y) * (1.0 - smoothstep(0.86, 0.92, vUv.y));
            // The green of the eyes, the ears and the shoulders glows brighter with the voice and breathes when quiet.
            float breath = 0.5 + 0.5 * sin(uTime * 1.4);
            float greenness = smoothstep(0.08, 0.3, color.g - max(color.r, color.b));
            float eyes = (1.0 - covered) * (exp(-length((vUv - EYE_LEFT) / vec2(1.0, 0.55)) / 0.05)
                                            + exp(-length((vUv - EYE_RIGHT) / vec2(1.0, 0.55)) / 0.05));
            vec3 green = vec3(0.25, 1.0, 0.4);
            color.rgb += green * greenness * (0.08 + 0.12 * breath + 0.7 * uOpen) * color.a;
            vec3 glow = green * eyes * (0.12 + 0.08 * breath + 0.8 * uOpen);
            color.rgb += glow;
            color.a = clamp(max(color.a, glow.g * 0.7), 0.0, 1.0);
            // A green scan line runs down the face now and then.
            float scan = smoothstep(0.012, 0.0, abs(fract(uTime * 0.09) * 1.6 - 0.3 - vUv.y));
            color.rgb += green * scan * 0.18 * color.a;
        #elif defined(KROBOT)
            // Softened light from the relief; the legs, cut by the frame, fade out at the bottom.
            color.rgb *= mix(1.0, vLight, 0.5);
            color *= 1.0 - smoothstep(0.9, 1.0, vUv.y);
            // The yellow eye glows with the voice, and breathes slowly when the robot is quiet; its glow spreads around.
            float breath = 0.5 + 0.5 * sin(uTime * 1.1);
            float level = 0.12 + 0.12 * breath + 1.1 * uOpen;
            vec3 amber = vec3(1.0, 0.72, 0.18);
            vec2 d = vUv - EYE;
            float r = length(d);
            float core = exp(-r * r / (0.012 * 0.012));
            float halo = exp(-r / 0.03);
            // While it speaks, an arc of light turns along the ring around the eye.
            float angle = atan(d.y, d.x);
            float arc = pow(0.5 + 0.5 * cos(angle - uTime * 4.0), 6.0);
            float ring = exp(-pow((r - EYE_RING * 0.82) / 0.005, 2.0)) * arc * uOpen;
            vec3 glow = amber * (core * 0.9 + halo * 0.5) * level + amber * ring * 1.2;
            color.rgb += glow;
            color.a = clamp(max(color.a, glow.g * 0.7), 0.0, 1.0);
        #elif defined(RIVET)
            // The steel shutter over the eyeball: the metal above the eye toned down to a mid grey (its worn light patches
            // would read as an empty eye), a little lighter at the top, darker at its lower edge.
            float shade = 1.05 - 0.2 * smoothstep(-1.0, 1.0, e.y) - 0.45 * smoothstep(0.12, 0.0, lid - e.y);
            vec3 steel = mix(shutter.rgb, vec3(0.3) * shutter.a, 0.55) * shade;
            color = mix(color, vec4(steel, shutter.a), covered);
            // Softened light from the relief; the antenna, cut by the top of the frame, and the body, cut at the bottom,
            // fade out.
            color.rgb *= mix(1.0, vLight, 0.5);
            color *= mix(1.0, smoothstep(0.0, 0.035, vUv.y), 1.0 - smoothstep(0.03, 0.05, abs(vUv.x - 0.39)));
            color *= 1.0 - smoothstep(0.9, 1.0, vUv.y);
            // A cold light flickers in the mouth slot while it speaks (in the dark of the slot only).
            vec2 s = tilted(vUv, SLOT) / vec2(0.075, 0.018);
            float slot = exp(-(s.x * s.x * s.x * s.x + s.y * s.y * s.y * s.y));
            float dark = 1.0 - smoothstep(0.08, 0.3, max(color.r, max(color.g, color.b)));
            float flicker = 0.8 + 0.2 * sin(uTime * 23.0) * sin(uTime * 7.0);
            color.rgb += vec3(0.82, 0.9, 1.0) * slot * dark * (0.04 + 0.95 * uOpen * flicker) * color.a;
            // Old film: the exposure flickers a little, and a fine grain changes 24 times a second.
            color.rgb *= 0.97 + 0.03 * sin(uTime * 31.0) * sin(uTime * 17.0);
            vec2 cell = floor(vUv * 700.0) + mod(floor(uTime * 24.0), 61.0) * vec2(1.7, 9.2);
            float grain = fract(sin(dot(cell, vec2(12.9898, 78.233))) * 43758.5453);
            color.rgb = max(color.rgb + (grain - 0.5) * 0.06 * color.a, vec3(0.0));
        #elif defined(SHIRO)
            // The eyelid over the eye, and the line of the lashes along its edge while it moves down.
            color = mix(color, vec4(cheek.rgb * 0.97, cheek.a), covered);
            float lash = 0.85 * exp(-pow((e.y - lid) / 0.12, 2.0)) * eyeArea * smoothstep(0.0, 0.15, blink) * (1.0 - smoothstep(0.65, 1.0, abs(e.x)));
            color.rgb = mix(color.rgb, vec3(0.07, 0.07, 0.09) * color.a, lash);
            // Softened light from the relief; the hair, cut by the top of the frame, and the armour, cut at the bottom,
            // fade out.
            color.rgb *= mix(1.0, vLight, 0.5);
            color *= smoothstep(0.0, 0.03, vUv.y) * (1.0 - smoothstep(0.9, 1.0, vUv.y));
            // The yellow green of the eyes glows brighter with the voice and breathes when he is quiet.
            float breath = 0.5 + 0.5 * sin(uTime * 1.4);
            vec3 green = vec3(0.5, 1.0, 0.2);
            float greenness = smoothstep(0.08, 0.3, color.g - max(color.r, color.b)) * (1.0 - covered);
            color.rgb += green * greenness * (0.06 + 0.1 * breath + 0.6 * uOpen) * color.a;
            float eyes = (1.0 - covered) * (exp(-length((vUv - IRIS_LEFT) / vec2(1.0, 0.75)) / 0.022)
                                            + exp(-length((vUv - IRIS_RIGHT) / vec2(1.0, 0.75)) / 0.022));
            vec3 glow = green * eyes * (0.08 + 0.06 * breath + 0.6 * uOpen);
            // A green light runs down the seam of his forehead: now and then when he is quiet, the seam lit as he speaks.
            float along = (vUv.y - SEAM_TOP) / (SEAM_BOTTOM - SEAM_TOP);
            float dx = abs(vUv.x - mix(0.4705, SEAM_X, clamp((vUv.y - SEAM_TOP) / 0.031, 0.0, 1.0)));
            float onSeam = smoothstep(-0.02, 0.0, along) * (1.0 - smoothstep(1.0, 1.03, along)) * (exp(-pow(dx / 0.0022, 2.0)) + 0.35 * exp(-dx / 0.007));
            float pulse = exp(-pow((along - (fract(uTime * 0.45) * 1.4 - 0.2)) / 0.07, 2.0));
            glow += green * onSeam * (pulse * (0.25 + 1.1 * uOpen) + 0.3 * uOpen) * color.a;
            color.rgb += glow;
            color.a = clamp(max(color.a, glow.g * 0.7), 0.0, 1.0);
        #elif defined(KINGYO)
            // The shutter over the eye: the metal above it toned to a deep orange (its light reflection would read as a
            // glowing eye), a little lighter at the top, darker at its edge.
            float shade = 1.05 - 0.2 * smoothstep(-1.0, 1.0, e.y) - 0.45 * smoothstep(0.12, 0.0, lid - e.y);
            vec3 metal = mix(shutter.rgb, vec3(0.78, 0.4, 0.05) * shutter.a, 0.6) * shade;
            color = mix(color, vec4(metal, shutter.a), covered);
            color.rgb *= mix(1.0, vLight, 0.45);
            // Bubbles rise from the mouth, clearer while it speaks: a blue ring (seen on a light or a dark ground) and a
            // white glint.
            vec2 bubbles = bubble(vUv, 0.0, uTime) + bubble(vUv, 1.0, uTime) + bubble(vUv, 2.0, uTime) + bubble(vUv, 3.0, uTime);
            float seen = 0.4 + 0.6 * uOpen;
            vec3 water = (vec3(0.4, 0.66, 0.95) * min(bubbles.x, 1.0) + vec3(1.0) * min(bubbles.y, 1.0)) * seen;
            float waterAlpha = clamp(max(bubbles.x * 0.8, bubbles.y) * seen, 0.0, 1.0);
            color = vec4(water + color.rgb * (1.0 - waterAlpha), waterAlpha + color.a * (1.0 - waterAlpha));
        #elif defined(HIKARI)
            // The eyelid: rounded (lighter at its middle, darker towards its edge), the lashes along its edge.
            float shade = 0.97 + 0.05 * (1.0 - clamp(dot(e, e), 0.0, 1.0)) - 0.06 * smoothstep(-0.2, lid, e.y);
            float lash = smoothstep(0.2, 0.0, abs(e.y - lid)) * smoothstep(0.2, 1.0, blink) * (1.0 - smoothstep(0.6, 0.95, abs(e.x)));
            vec3 eyelid = mix(skin.rgb * shade, vec3(0.13, 0.09, 0.07) * skin.a, 0.7 * lash);
            color = mix(color, vec4(eyelid, skin.a), covered);
            // Softened light from the relief (the photo has its own); the hair cut by the top of the frame, and the
            // armour and the hair cut at the bottom and on the sides, fade out.
            color.rgb *= mix(1.0, vLight, 0.6);
            color *= smoothstep(0.0, 0.03, vUv.y) * (1.0 - smoothstep(0.9, 1.0, vUv.y)) * smoothstep(0.0, 0.06, vUv.x) * (1.0 - smoothstep(0.94, 1.0, vUv.x));
            // Her green irises shine brighter with the voice and breathe when she is quiet; the stone of her forehead and
            // the light of her shoulder glow green, the shoulder light pulsing slowly.
            float breath = 0.5 + 0.5 * sin(uTime * 1.3);
            vec3 green = vec3(0.3, 1.0, 0.45);
            float greenness = clamp((color.g - max(color.r, color.b)) * 4.0, 0.0, 1.0);
            float irises = (1.0 - smoothstep(IRIS_R * 0.8, IRIS_R * 1.05, length(vUv - IRIS_LEFT)))
                           + (1.0 - smoothstep(IRIS_R * 0.8, IRIS_R * 1.05, length(vUv - IRIS_RIGHT)));
            color.rgb += green * greenness * irises * (1.0 - covered) * (0.06 + 0.06 * breath + 0.7 * uOpen) * color.a;
            float stone = exp(-length(vUv - STONE) / 0.01);
            float led = exp(-length(vUv - LED) / 0.007);
            vec3 glow = green * (stone * (0.12 + 0.1 * breath + 1.0 * uOpen) + led * (0.25 + 0.5 * (0.5 + 0.5 * sin(uTime * 2.2))));
            color.rgb += glow;
            color.a = clamp(max(color.a, glow.g * 0.7), 0.0, 1.0);
        #elif defined(KAMEN)
            color.rgb *= mix(1.0, vLight, 0.5);
            float breath = 0.5 + 0.5 * sin(uTime * 1.2);
            vec3 red = vec3(1.0, 0.12, 0.16);
            // The red marks of the mask glow with the voice and breathe when it is quiet.
            float redness = clamp((color.r - max(color.g, color.b) - 0.12) * 3.5, 0.0, 1.0);
            color.rgb += red * redness * (0.04 + 0.05 * breath + 0.55 * uOpen) * color.a;
            // In the dark of the eye holes two red embers look elsewhere every 3.4 s, brighter as it speaks; the inside
            // of the mouth glows red.
            float dark = 1.0 - smoothstep(0.06, 0.25, max(color.r, max(color.g, color.b)));
            float glance = uTime / 3.4;
            vec2 look = mix(gaze(floor(glance) - 1.0), gaze(floor(glance)), smoothstep(0.0, 0.08, fract(glance))) * vec2(0.035, 0.008);
            vec2 toLeft = vUv - EYE_LEFT - look;
            vec2 toRight = vUv - EYE_RIGHT - look;
            float embers = exp(-pow(length(toLeft * vec2(1.0, 1.4)) / 0.008, 2.0)) + 0.35 * exp(-length(toLeft) / 0.018)
                           + exp(-pow(length(toRight * vec2(1.0, 1.4)) / 0.008, 2.0)) + 0.35 * exp(-length(toRight) / 0.018);
            // (The glow of the mouth stays deep inside: lit all over, the hole would read as red lips.)
            vec2 inMouth = (vUv - MOUTH) / vec2(0.05, 0.014);
            float mouthGlow = exp(-dot(inMouth, inMouth));
            color.rgb += red * dark * (embers * (0.25 + 0.15 * breath + 1.1 * uOpen) + mouthGlow * (0.03 + 0.4 * uOpen)) * color.a;
            // The light under the chin pulses, brighter as it speaks.
            vec3 beacon = red * exp(-length(vUv - CHIN_LIGHT) / 0.006) * (0.4 + 0.3 * sin(uTime * 3.0) + uOpen);
            color.rgb += beacon;
            color.a = clamp(max(color.a, beacon.r * 0.8), 0.0, 1.0);
            // A digital rain behind the mask, where its strands of hair were: thin streaks of red, white at their heads,
            // fall each at its own pace, more lit as it speaks.
            float column = floor(vUv.x * 110.0);
            float h1 = fract(sin(column * 12.9898) * 43758.5453);
            float h2 = fract(sin(column * 78.233) * 43758.5453);
            float lit = step(0.7, h1) * smoothstep(0.17, 0.23, vUv.x) * (1.0 - smoothstep(0.8, 0.86, vUv.x));
            float head = fract(uTime * (0.08 + 0.12 * h2) + h1 * 7.0) * 1.3 - 0.15;
            float trail = vUv.y < head ? exp(-(head - vUv.y) / 0.07) : 0.0;
            float thin = exp(-pow((fract(vUv.x * 110.0) - 0.5) / 0.18, 2.0));
            float sparkle = 0.6 + 0.4 * sin(uTime * 20.0 + column * 3.0 + vUv.y * 60.0);
            float rain = lit * trail * thin * sparkle * (0.35 + 0.5 * uOpen);
            vec3 drops = mix(red, vec3(1.0), smoothstep(0.02, 0.0, head - vUv.y)) * rain;
            color += vec4(drops, rain * 0.9) * (1.0 - color.a);
        #elif defined(AOI)
            // Light from the relief and the faint scan lines of a hologram.
            color.rgb *= mix(1.0, vLight, 0.5) * (0.95 + 0.05 * sin(gl_FragCoord.y * 2.2));
            float breath = 0.5 + 0.5 * sin(uTime * 1.3);
            vec3 cyan = vec3(0.35, 0.75, 1.0);
            float lum = max(color.r, max(color.g, color.b)) / max(color.a, 0.001);
            float blueness = clamp((color.b - max(color.r, color.g)) / max(color.a, 0.001) * 3.0, 0.0, 1.0);
            // Its small lights twinkle, each on its own (cells of the picture), and the bright circuits flare with the voice.
            vec2 cell = floor(vUv * 90.0);
            float twinkle = fract(sin(dot(cell + mod(floor(uTime * 5.0), 37.0) * vec2(3.1, 7.7), vec2(12.9898, 78.233))) * 43758.5453);
            float bright = smoothstep(0.55, 0.9, lum);
            color.rgb *= 1.0 + bright * (0.5 * twinkle - 0.2);
            color.rgb += cyan * bright * (0.08 + 0.06 * breath + 0.7 * uOpen) * color.a;
            // Waves of data run up through the circuits, one after the other, brighter as it speaks.
            float wave = vUv.y + 0.04 * sin(vUv.x * 9.0 + uTime) - (1.1 - fract(uTime * 0.3) * 1.3);
            float data = exp(-wave * wave / 0.0012) * smoothstep(0.15, 0.45, lum) * (0.4 + 0.6 * blueness);
            color.rgb += cyan * data * (0.25 + 0.9 * uOpen) * color.a;
            // The closed eyes light up as it speaks, a light running along them.
            vec2 eL = (vUv - EYE_LEFT) / EYE_HALF;
            vec2 eR = (vUv - EYE_RIGHT) / EYE_HALF;
            float eyes = exp(-dot(eL, eL)) + exp(-dot(eR, eR));
            float run = exp(-pow(fract(uTime * 0.6) * 3.0 - 1.5 - (vUv.x < 0.5 ? eL.x : eR.x), 2.0) * 4.0);
            vec3 glow = cyan * eyes * (0.12 + 0.1 * breath + 0.9 * uOpen + 0.4 * run * uOpen);
            color.rgb += glow;
            color.a = clamp(max(color.a, glow.b * 0.6), 0.0, 1.0);
        #elif defined(HAGANE)
            // The eyelid: rounded, the lashes along its edge.
            float shade = 0.97 + 0.05 * (1.0 - clamp(dot(e, e), 0.0, 1.0)) - 0.06 * smoothstep(-0.2, lid, e.y);
            float lash = smoothstep(0.22, 0.0, abs(e.y - lid)) * smoothstep(0.2, 1.0, blink) * (1.0 - smoothstep(0.6, 0.95, abs(e.x)));
            color = mix(color, vec4(mix(skin.rgb * shade, vec3(0.08, 0.06, 0.06) * skin.a, 0.75 * lash), skin.a), covered);
            // Softened light from the relief (the photo has its own); what the frame cuts at the top and the bottom fades.
            color.rgb *= mix(1.0, vLight, 0.6);
            color *= smoothstep(0.0, 0.03, vUv.y) * (1.0 - smoothstep(0.9, 1.0, vUv.y));
            float lum = max(color.r, max(color.g, color.b)) / max(color.a, 0.001);
            // The red wires of her neck and shoulders glow as she speaks (not her lips, above them).
            float redness = clamp((color.r - max(color.g, color.b)) / max(color.a, 0.001) * 3.0 - 0.3, 0.0, 1.0) * smoothstep(0.585, 0.6, vUv.y);
            color.rgb += vec3(1.0, 0.25, 0.12) * redness * (0.05 + 0.04 * sin(uTime * 1.4) + 0.8 * uOpen) * color.a;
            // A gleam runs over the steel of her machinery and her armour every 5 s (the grey, light parts; not her
            // face nor her hair).
            float grey = 1.0 - clamp((max(color.r, max(color.g, color.b)) - min(color.r, min(color.g, color.b))) / max(color.a, 0.001) * 6.0, 0.0, 1.0);
            float steel = grey * smoothstep(0.3, 0.6, lum) * (1.0 - (1.0 - smoothstep(0.17, 0.21, abs(vUv.x - 0.495))) * (1.0 - smoothstep(0.58, 0.64, vUv.y)));
            float sweep = vUv.x * 0.8 + vUv.y * 0.6 - (fract(uTime / 5.0) * 2.4 - 0.4);
            color.rgb += vec3(1.0, 0.97, 0.92) * steel * exp(-sweep * sweep / 0.0015) * 0.45 * color.a;
        #elif defined(SORA)
            // The eyelid: rounded, a little darker than the lit skin it is read from, darker towards the lashes and in the
            // shadow of the crease at its top.
            float shade = 0.9 + 0.05 * (1.0 - clamp(dot(e, e), 0.0, 1.0)) - 0.08 * smoothstep(-0.4, 0.0, e.y - lashLine)
                          - 0.07 * smoothstep(-0.55, -1.0, e.y);
            float lash = exp(-pow((e.y - lashLine) / 0.09, 2.0)) * smoothstep(0.1, 0.5, blink) * (1.0 - smoothstep(0.7, 1.0, abs(e.x)));
            color = mix(color, vec4(mix(skin.rgb * shade, vec3(0.1, 0.07, 0.05) * skin.a, 0.85 * lash), skin.a), covered);
            // Softened light from the relief (the picture has its own); the hair cut by the top of the frame and the suit
            // cut at the bottom fade out.
            color.rgb *= mix(1.0, vLight, 0.55);
            color *= smoothstep(0.0, 0.03, vUv.y) * (1.0 - smoothstep(0.9, 1.0, vUv.y));
            float breath = 0.5 + 0.5 * sin(uTime * 1.3);
            vec3 cyan = vec3(0.3, 0.9, 1.0);
            // The teal of her irises shines a little brighter with the voice.
            float teal = clamp((min(color.g, color.b) - color.r) / max(color.a, 0.001) * 3.0, 0.0, 1.0);
            float irises = (1.0 - smoothstep(IRIS_R * 1.0, IRIS_R * 1.3, length(vUv - IRIS_LEFT)))
                           + (1.0 - smoothstep(IRIS_R * 1.0, IRIS_R * 1.3, length(vUv - IRIS_RIGHT)));
            color.rgb += cyan * teal * irises * (1.0 - covered) * (0.04 + 0.04 * breath + 0.5 * uOpen) * color.a;
            // The cyan ring of her chest glows with the voice and breathes when she is quiet.
            float badge = teal * (1.0 - smoothstep(0.04, 0.06, length(vUv - BADGE)));
            color.rgb += cyan * badge * (0.08 + 0.1 * breath + 0.7 * uOpen) * color.a;
            // While she speaks an arc of light turns around the lens of her earpiece, its glass lit faintly cyan; when
            // she is quiet the ring breathes.
            vec2 l = (vUv - LENS) / LENS_HALF;
            float r = length(l);
            float arc = pow(0.5 + 0.5 * cos(atan(l.y, l.x) - uTime * 3.0), 6.0);
            float ring = exp(-pow((r - 0.88) / 0.1, 2.0));
            float glass = 1.0 - smoothstep(0.6, 0.8, r);
            vec3 glow = cyan * (ring * (0.06 + 0.06 * breath + arc * (0.2 + 1.5 * uOpen)) + glass * 0.25 * uOpen) * color.a;
            // The light at the base of her neck pulses slowly, brighter as she speaks; its glow spreads around.
            float neck = exp(-length(vUv - NECK_LIGHT) / 0.012);
            glow += cyan * neck * (0.25 + 0.15 * breath + 0.9 * uOpen);
            color.rgb += glow;
            color.a = clamp(max(color.a, glow.b * 0.7), 0.0, 1.0);
        #elif defined(TAMA)
            // A blink every 5.2 s: the six blades of an iris diaphragm close over each lens and open again, turning as
            // they go: dark steel, every other blade a little lighter, a glint along the edge of the opening.
            float blinkPhase = fract(uTime / 5.2);
            float blink = blinkPhase < 0.06 ? sin(blinkPhase / 0.06 * 3.14159) : 0.0;
            vec2 toLens = vUv - eyeLens;
            float rv = length(toLens) / lensR;
            float sector = 6.2831853 / 6.0;
            float bladeAngle = atan(toLens.y, toLens.x) + 0.8 * blink;
            float inSector = mod(bladeAngle, sector) - 0.5 * sector;
            float poly = rv * cos(inSector) / cos(0.5 * sector);   // 1 on a hexagon of radius 1
            float opening = 1.2 * (1.0 - blink);
            float shut = blink > 0.0 ? smoothstep(opening, opening + 0.04, poly) * (1.0 - smoothstep(0.96, 1.02, rv)) : 0.0;
            float blade = mod(floor((bladeAngle + 1.3 * rv) / sector), 2.0);
            vec3 steel = vec3(0.16, 0.17, 0.19) * (0.8 + 0.25 * blade + 0.2 * (1.0 - rv))
                         + vec3(0.6, 0.65, 0.7) * 0.5 * exp(-pow((poly - opening) / 0.05, 2.0));
            color = mix(color, vec4(steel * color.a, color.a), shut);
            // Softened light from the relief; the body, cut by the frame at the bottom and on the sides, fades out.
            color.rgb *= mix(1.0, vLight, 0.5);
            color *= (1.0 - smoothstep(0.9, 1.0, vUv.y)) * smoothstep(0.0, 0.06, vUv.x) * (1.0 - smoothstep(0.94, 1.0, vUv.x));
            float breath = 0.5 + 0.5 * sin(uTime * 1.2);
            vec3 orange = vec3(1.0, 0.5, 0.15);
            float lum = max(color.r, max(color.g, color.b)) / max(color.a, 0.001);
            // The cores glow brighter with the voice and breathe when it is quiet (where they look), the blue rings
            // shine as it speaks; the closed blades hide both.
            float rc = length(vUv - core - look * lensR * 0.06) / lensR;
            float cores = exp(-pow(rc / 0.2, 2.0)) + 0.35 * exp(-rc / 0.25);
            vec3 glow = orange * cores * (0.12 + 0.12 * breath + 0.9 * uOpen) * (1.0 - shut);
            float blueness = clamp((color.b - color.r) / max(color.a, 0.001) * 2.5 - 0.3, 0.0, 1.0) * (1.0 - smoothstep(0.85, 0.95, rv));
            glow += vec3(0.3, 0.75, 1.0) * blueness * (0.05 + 0.08 * breath + 0.6 * uOpen) * (1.0 - shut) * color.a;
            // The grille speaks: its cells light up orange column by column with the voice, like a level meter (the
            // lower row first, the upper one for the loud sounds), its middle the highest; a faint glow when it is quiet.
            vec2 g = (vUv - GRILLE) / GRILLE_HALF;
            float inGrille = 1.0 - smoothstep(0.8, 1.0, pow(abs(g.x), 4.0) + g.y * g.y);
            float cell = smoothstep(0.1, 0.17, lum) * (1.0 - smoothstep(0.4, 0.6, lum));
            float column = floor((vUv.x - 0.5005) / GRILLE_COLUMN);
            float jitter = fract(sin(dot(vec2(column, floor(uTime * 12.0)), vec2(12.9898, 78.233))) * 43758.5453);
            float level = uOpen * (1.1 - 0.2 * abs(column + 0.5)) * (0.5 + 0.5 * jitter);
            float needed = vUv.y > GRILLE_BAR ? 0.1 : 0.45;
            glow += orange * inGrille * cell * (0.05 + 0.04 * breath + 0.9 * smoothstep(needed, needed + 0.08, level)) * color.a;
            // The two small lights of the chest pulse, brighter as it speaks.
            float leds = exp(-length(vUv - CHEST_LEFT) / 0.006) + exp(-length(vUv - CHEST_RIGHT) / 0.006);
            glow += vec3(1.0, 0.75, 0.4) * leds * (0.3 + 0.25 * sin(uTime * 2.4) + 0.8 * uOpen);
            color.rgb += glow;
            color.a = clamp(max(color.a, max(glow.r, glow.b) * 0.7), 0.0, 1.0);
        #elif defined(BENI)
            // The drawn eyes, moved and squashed, on the white of the screen.
            color = mix(color, mix(screenWhite, drawnEye, inEye), eyeArea);
            // Its mouth, drawn under the smile, opens with the voice: dark, a pink tongue at its bottom.
            float smile = SMILE_Y - SMILE_BEND * (vUv.x - SMILE_X) * (vUv.x - SMILE_X);
            float across = (vUv.x - TALK.x) / TALK.y;
            float drop = 0.034 * uOpen * max(0.0, 1.0 - across * across);
            float underSmile = vUv.y - smile;
            float talking = smoothstep(-0.004, -0.002, underSmile) * (1.0 - smoothstep(drop - 0.0025, drop + 0.0005, underSmile))
                            * (1.0 - smoothstep(0.9, 1.0, abs(across))) * smoothstep(0.02, 0.12, uOpen);
            vec3 inMouth = mix(vec3(0.1, 0.08, 0.1), vec3(0.9, 0.36, 0.42), smoothstep(0.5, 0.85, underSmile / max(drop, 0.001)));
            color.rgb = mix(color.rgb, inMouth * color.a, talking);
            // Softened light from the relief; the legs, cut by the bottom of the frame, fade out.
            color.rgb *= mix(1.0, vLight, 0.45);
            color *= 1.0 - smoothstep(0.9, 1.0, vUv.y);
            // The blue glints of its eyes and the light of its chest glow with the voice and breathe when it is quiet.
            float breath = 0.5 + 0.5 * sin(uTime * 1.4);
            vec3 blue = vec3(0.35, 0.8, 1.0);
            vec2 glint = eye + look + ((leftEye ? GLINT_LEFT : GLINT_RIGHT) - eye) * vec2(1.0, squash);
            vec3 glow = blue * exp(-pow(length((vUv - glint) / vec2(1.0, squash)) / 0.018, 2.0)) * squash
                        * (0.12 + 0.1 * breath + 0.8 * uOpen) * color.a;
            float fromChest = length(vUv - CHEST) / CHEST_R;
            float chestLight = (1.0 - smoothstep(0.8, 1.0, fromChest)) * (0.08 + 0.1 * breath + 0.6 * uOpen) * color.a
                               + exp(-fromChest / 0.4) * (0.08 + 0.06 * breath + 0.45 * uOpen);
            glow += blue * chestLight;
            color.rgb += glow;
            color.a = clamp(max(color.a, glow.b * 0.7), 0.0, 1.0);
        #elif defined(MUSUKO)
            if (covered > 0.0)
            {
                // Skin of the eyelid: the average of the skin above and below the eye, with the lashes at its edge.
                vec4 skin = 0.5 * (texture(uFace, vec2(uv.x, eye.y - 0.036)) + texture(uFace, vec2(uv.x, eye.y + 0.05)));
                float lash = smoothstep(0.22, 0.0, abs(e.y - lid)) * (1.0 - smoothstep(0.7, 1.0, abs(e.x)));
                vec4 eyelid = vec4(mix(skin.rgb * 0.92, vec3(0.03, 0.04, 0.07) * skin.a, lash), skin.a);
                color = mix(color, eyelid, covered);
            }
            // Softened light from the relief; the suit, cut by the frame at the bottom and on the sides, fades out.
            color.rgb *= mix(1.0, vLight, 0.5);
            color *= (1.0 - smoothstep(0.9, 1.0, vUv.y)) * smoothstep(0.08, 0.16, vUv.x) * (1.0 - smoothstep(0.84, 0.92, vUv.x));
            // The green irises and the lights of the suit glow with the voice, and breathe slowly when he is quiet.
            float breath = 0.5 + 0.5 * sin(uTime * 1.6);
            float irises = (1.0 - covered) * light(vUv, EYE_LEFT, 0.012) + light(vUv, EYE_RIGHT, 0.012);
            color.rgb += vec3(0.25, 1.0, 0.35) * irises * (0.1 + 0.6 * uOpen) * color.a;
            float lights = light(vUv, vec2(0.5, 0.762), 0.03) + light(vUv, vec2(0.51, 0.985), 0.04)
                           + 0.7 * (light(vUv, vec2(0.24, 0.86), 0.018) + light(vUv, vec2(0.31, 0.835), 0.018)
                                    + light(vUv, vec2(0.695, 0.82), 0.018) + light(vUv, vec2(0.755, 0.835), 0.018));
            vec3 glow = vec3(0.3, 0.95, 1.0) * lights * (0.15 + 0.2 * breath + 0.9 * uOpen);
            color.rgb += glow;
            color.a = clamp(max(color.a, max(glow.g, glow.b) * 0.8), 0.0, 1.0);
        #elif defined(KILLER)
            // Softened light from the relief; the body, cut by the frame at the bottom, fades out.
            color.rgb *= mix(1.0, vLight, 0.55);
            color *= 1.0 - smoothstep(0.92, 1.0, vUv.y);
            // A glint runs along the blade every few seconds (on the grey steel only, not on the blood).
            float steel = 1.0 - smoothstep(0.08, 0.2, max(color.r, max(color.g, color.b)) - min(color.r, min(color.g, color.b)));
            vec2 axis = normalize(BLADE_HEEL - BLADE_TIP);
            float along = dot(vUv - BLADE_TIP, axis) / length(BLADE_HEEL - BLADE_TIP);
            float sweep = fract(uTime * 0.22) * 2.2 - 0.6;
            float g = (along - sweep) / 0.05;
            float glint = exp(-g * g) * (1.0 - smoothstep(0.03, 0.05, bladeDistance(vUv)));
            color.rgb += vec3(1.0) * glint * steel * 0.8 * color.a;
            // The hearts of the chest glow pink with the voice, and beat slowly when she is quiet.
            float beat = pow(0.5 + 0.5 * sin(uTime * 2.6), 4.0);
            float hearts = exp(-length(vUv - vec2(0.5, 0.835)) / 0.018) + exp(-length(vUv - vec2(0.5, 0.935)) / 0.03);
            color.rgb += vec3(1.0, 0.35, 0.75) * hearts * (0.15 + 0.25 * beat + 0.9 * uOpen);
            // During a glitch the picture splits into red and cyan.
            float glitching = step(uGlitch.x, 90.0);
            color.r += glitching * 0.25 * texture(uFace, uv + vec2(0.006, 0.0)).a;
            color.gb *= 1.0 - glitching * 0.15;
        #elif defined(HAL)
            // Cut out in a circle around the housing, lit from the relief; the red core, its halo and the horizontal
            // flare glow brighter with the voice (added light, also beyond the circle).
            float r = length(vUv - EYE);
            color *= mix(1.0, vLight, 0.5) * (1.0 - smoothstep(0.46, 0.5, r));
            float breath = 0.5 + 0.5 * sin(uTime * 1.3);
            float core = exp(-r * r / 0.0012) * (0.35 + 0.9 * uOpen + 0.15 * breath);
            float halo = exp(-r / 0.07) * (0.12 + 0.5 * uOpen + 0.05 * breath);
            float flare = exp(-abs(vUv.y - EYE.y) * 160.0) * exp(-abs(vUv.x - EYE.x) * 4.0) * (0.25 + 0.75 * uOpen);
            vec3 glow = vec3(1.0, 0.18, 0.08) * (halo + flare) + vec3(1.0, 0.75, 0.55) * core;
            color.rgb += glow;
            color.a = clamp(max(color.a, max(glow.r, max(glow.g, glow.b))), 0.0, 1.0);
        #else
            // Light from the relief, faint scan lines and a slow scan glow running down.
            float scanLines = 0.94 + 0.06 * sin(gl_FragCoord.y * 2.2);
            float scan = 0.08 * smoothstep(0.06, 0.0, abs(fract(uTime * 0.12) * 1.4 - 0.2 - uv.y));
            color.rgb *= vLight * scanLines;
            color.rgb += scan * color.a * vec3(0.55, 0.85, 1.0);
        #endif
            fragColor = color;
        }
        """;
}

/// <summary>Shaders of <see cref="JarvisScene"/>: a full-band quad and the fragment shader that casts a ray per pixel.</summary>
public static class JarvisShaders
{
    public const string VertexShader = """
        in vec2 aPos;

        void main()
        {
            gl_Position = vec4(aPos, 0.0, 1.0);
        }
        """;

    // Camera on the z axis looking at the origin; the hologram is centred on the origin, the reticle in the plane z = 0
    // facing the camera, the gyroscope rings in tilted planes through the origin, the sphere at the origin. Sizes are
    // in scene units: with CAMERA / FOCAL = 2, a radius of 0.92 fills 0.46 of the band height.
    public const string FragmentShader = """
        uniform vec3 uFrame;      // width, height (pixels), time (seconds)
        uniform float uLevel;     // voice level, 0 to 1
        uniform float uLight;     // 1 when the band background is light
        uniform vec3 uGlitch;     // row (share of the height), sideways shift (pixels)
        out vec4 fragColor;

        const float PI = 3.14159265;
        const float TAU = 6.28318531;
        const float CAMERA = 3.4;
        const float FOCAL = 1.7;

        vec3 gColor;   // sum of the colors of the elements crossed by the ray
        float gGlow;   // sum of their intensities

        void add(vec3 color, float amount)
        {
            gColor += color * amount;
            gGlow += amount;
        }

        float hash(float n) { return fract(sin(n) * 43758.5453); }

        // 1 on a line at distance 0, fading to 0 at distance w.
        float lineAt(float d, float w) { return 1.0 - smoothstep(0.0, w, abs(d)); }

        // 1 between a and b, with soft edges of width w.
        float bandAt(float x, float a, float b, float w) { return smoothstep(a - w, a, x) * (1.0 - smoothstep(b, b + w, x)); }

        // n radial marks per turn (a = angle in turns), turned by spin: 1 on a mark.
        float marks(float a, float r, float n, float spin, float w)
        {
            float f = fract(a * n + spin);
            return lineAt(min(f, 1.0 - f) / n * TAU * r, w);
        }

        mat3 rotX(float a) { float c = cos(a); float s = sin(a); return mat3(1.0, 0.0, 0.0, 0.0, c, s, 0.0, -s, c); }
        mat3 rotY(float a) { float c = cos(a); float s = sin(a); return mat3(c, 0.0, -s, 0.0, 1.0, 0.0, s, 0.0, c); }
        mat3 rotZ(float a) { float c = cos(a); float s = sin(a); return mat3(c, s, 0.0, -s, c, 0.0, 0.0, 0.0, 1.0); }

        // Where the ray crosses the plane z = 0 of a layer turned by m (p in the layer, t along the ray).
        bool plane(vec3 ro, vec3 rd, mat3 m, out vec2 p, out float t)
        {
            vec3 o = ro * m;   // transpose(m) * ro: into the layer
            vec3 d = rd * m;
            p = vec2(0.0);
            t = 0.0;
            if (abs(d.z) < 0.0001)
                return false;
            t = -o.z / d.z;
            p = o.xy + t * d.xy;
            return t > 0.0;
        }

        // A gyroscope ring tilted by m: a thin ring, dashes and three particles orbiting on it.
        void gyroscope(vec3 ro, vec3 rd, mat3 m, float radius, float speed, vec3 color, float w)
        {
            vec2 p;
            float t;
            if (!plane(ro, rd, m, p, t))
                return;
            float fade = clamp(1.3 - 0.6 * t / CAMERA, 0.35, 1.0);   // the far side of the ring is dimmer
            float r = length(p);
            float a = atan(p.y, p.x) / TAU + 0.5;
            add(color, fade * 0.55 * lineAt(r - radius, w));
            add(color, fade * 0.3 * lineAt(r - radius - 0.035, w) * step(0.5, fract(a * 24.0 + uFrame.z * speed * 0.2)));
            for (int i = 0; i < 3; i++)
            {
                float angle = uFrame.z * speed + float(i) * TAU / 3.0;
                float d = length(p - radius * vec2(cos(angle), sin(angle)));
                add(color, fade * 0.0009 / (d * d + 0.0006));
            }
        }

        void main()
        {
            vec2 res = uFrame.xy;
            float time = uFrame.z;
            vec2 frag = gl_FragCoord.xy;
            if (abs(frag.y / res.y - uGlitch.x) < 0.012)
                frag.x += uGlitch.y;

            float unit = min(res.x, res.y);
            vec2 uv = (frag - 0.5 * res) / unit;
            float px = 1.0 / unit;
            float w = 1.6 * px * CAMERA / FOCAL;   // about 1.6 pixel in the plane of the origin
            float level = uLevel;

            vec3 cyan = mix(vec3(0.30, 0.82, 1.00), vec3(0.00, 0.42, 0.72), uLight);
            vec3 ice = mix(vec3(0.75, 0.95, 1.00), vec3(0.00, 0.30, 0.55), uLight);
            vec3 amber = mix(vec3(1.00, 0.62, 0.20), vec3(0.85, 0.40, 0.00), uLight);
            gColor = vec3(0.0);
            gGlow = 0.0;

            vec3 ro = vec3(0.0, 0.0, CAMERA);
            vec3 rd = normalize(vec3(uv, -FOCAL));
            // The whole hologram sways slightly, as if it floated.
            mat3 sway = rotY(0.14 * sin(time * 0.31)) * rotX(0.07 * sin(time * 0.23));

            // Faint dot grid behind the hologram.
            vec2 cell = fract(frag / (unit * 0.055)) - 0.5;
            add(cyan, 0.07 * lineAt(length(cell), 0.06));

            // Reticle facing the viewer.
            vec2 p;
            float t;
            if (plane(ro, rd, sway, p, t))
            {
                float r = length(p);
                float a = atan(p.y, p.x) / TAU + 0.5;   // angle in turns
                add(cyan, 0.5 * lineAt(r - 0.80, w));
                add(cyan, 0.45 * marks(a, r, 90.0, time * 0.01, w) * bandAt(r, 0.72, 0.76, w));
                add(cyan, 0.7 * marks(a, r, 12.0, time * 0.01, 1.5 * w) * bandAt(r, 0.69, 0.78, w));
                add(ice, 0.8 * marks(a, r, 4.0, 0.0, 1.5 * w) * bandAt(r, 0.84, 0.92, w));
                add(cyan, 0.4 * lineAt(r - 0.66, 1.4 * w) * step(0.35, fract(a * 36.0 - time * 0.08)));

                // Amber arcs: three of them, longer with the voice.
                float arc = fract(a * 3.0 + time * 0.05);
                add(amber, 0.85 * bandAt(r, 0.585, 0.615, w) * bandAt(arc, 0.0, 0.10 + 0.20 * level, 0.004));
                add(amber, 0.35 * lineAt(r - 0.63, w) * bandAt(fract(a * 3.0 - time * 0.03 + 0.5), 0.0, 0.25, 0.004));

                // Inner double ring, flickering blocks and a radar sweep.
                add(cyan, 0.5 * (lineAt(r - 0.48, w) + lineAt(r - 0.465, w)));
                float block = floor(a * 60.0);
                add(ice, bandAt(r, 0.385, 0.405, w) * step(0.3, fract(a * 60.0))
                         * (0.15 + 0.6 * level * step(0.4, hash(block + floor(time * 10.0)))));
                add(cyan, 0.35 * pow(fract(a - time * 0.25), 6.0) * bandAt(r, 0.30, 0.46, w));
            }

            // Two gyroscope rings tilted in 3D around the core.
            gyroscope(ro, rd, sway * rotY(time * 0.35) * rotX(1.25), 0.34, 1.1, ice, w);
            gyroscope(ro, rd, sway * rotZ(-0.6) * rotX(time * 0.27) * rotY(1.15), 0.30, -0.8, cyan, w);

            // Wireframe sphere around the core.
            float radius = 0.17 + 0.012 * level;
            float b = dot(ro, rd);
            float h = b * b - dot(ro, ro) + radius * radius;
            if (h > 0.0)
            {
                vec3 n = (ro + (-b - sqrt(h)) * rd) / radius;
                vec3 q = n * rotY(time * 0.6) * rotX(0.4);
                float lat = acos(clamp(q.y, -1.0, 1.0)) / PI;
                float lon = atan(q.z, q.x) / TAU + 0.5;
                float sw = 1.4 * px / radius;
                float grid = lineAt((fract(lat * 7.0 + 0.5) - 0.5) / 7.0 * PI, sw)
                             + lineAt((fract(lon * 12.0 + 0.5) - 0.5) / 12.0 * TAU * sin(lat * PI), sw);
                float rim = pow(1.0 - abs(dot(n, rd)), 3.0);
                add(cyan, 0.3 * min(grid, 1.0) + 0.5 * rim);
            }

            // Glowing core, pulsing with the voice.
            float d = length(cross(rd, -ro));
            float pulse = 0.5 + 0.9 * level + 0.06 * sin(time * 3.0);
            add(ice, 0.0028 * pulse / (d * d + 0.0012));

            // Voice spectrum on both sides, when the band is wide enough.
            float halfWidth = 0.5 * res.x / unit;
            float start = 0.56;
            float span = halfWidth - start - 0.06;
            float ax = abs(uv.x) - start;
            if (span > 0.15 && ax > 0.0 && ax < span)
            {
                float barStep = 0.022;
                float i = floor(ax / barStep);
                float side = uv.x < 0.0 ? 57.0 : 0.0;
                float wave = 0.5 + 0.5 * sin(i * 0.9 + time * 7.0 + hash(i + side) * TAU);
                float envelope = 1.0 - ax / span;
                float barHeight = 0.015 + (0.04 + 0.34 * level) * wave * envelope;
                float bar = step(fract(ax / barStep), 0.55) * (1.0 - smoothstep(barHeight, barHeight + px, abs(uv.y)));
                add(cyan, 0.5 * bar * (0.6 + 0.4 * envelope));
                add(cyan, 0.25 * lineAt(uv.y, px) * envelope);
                // Brackets above and below the spectrum.
                add(cyan, 0.3 * lineAt(abs(uv.y) - 0.42, px) * step(ax, 0.25));
            }

            // Scan lines.
            float scan = 0.9 + 0.1 * sin(frag.y * 2.0);
            gColor *= scan;
            gGlow *= scan;

            // Dark band: the hologram glows (added light); light band: deep colors are painted over it.
            vec3 glowColor = min(gColor, vec3(1.0));
            float glowAlpha = clamp(max(gColor.r, max(gColor.g, gColor.b)), 0.0, 1.0);
            float paintAlpha = clamp(gGlow, 0.0, 1.0);
            vec3 paintColor = gColor / max(gGlow, 0.0001) * paintAlpha;
            fragColor = mix(vec4(glowColor, glowAlpha), vec4(paintColor, paintAlpha), uLight);
        }
        """;
}
