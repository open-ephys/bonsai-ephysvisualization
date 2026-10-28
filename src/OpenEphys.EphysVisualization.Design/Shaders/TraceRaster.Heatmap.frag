// Fills each visible channel's row with the color of its signal: at each pixel column, whichever of the min and
// max lies further from zero, with its sign, through a diverging colormap black at zero. Magnitudes up to the
// threshold are black.

// Each channel's color, with alpha 0 where its row is not filled.
uniform sampler2D colorTexture;

// The colormap, its two halves for the two signs meeting in the middle.
uniform sampler2D colormapTexture;
uniform int colormapLength;

uniform int firstVisible;
uniform int lastVisible;
uniform float origin;
uniform float rowHeight;
uniform float range;
uniform float threshold;

out vec4 fragmentColor;

void main()
{
    int p = int(gl_FragCoord.x);
    float y = floor(gl_FragCoord.y);

    // NB: row edges fall on whole pixels, as the plot's do. The row is the last visible one whose top is at or
    // above the pixel, found by binary search since rows never move up. Where rows share a slot it is the one
    // drawn last, as with the traces; a pixel below its bottom is in a gap.
    int lo = firstVisible - 1;
    int hi = lastVisible - 1;
    while (lo < hi)
    {
        int mid = (lo + hi + 1) / 2;
        if (floor(SlotTop(Slot(mid), origin, rowHeight)) <= y)
            lo = mid;
        else
            hi = mid - 1;
    }

    int channel = lo;
    if (channel < firstVisible || y >= floor(SlotTop(Slot(channel) + 1.0, origin, rowHeight)) ||
        texelFetch(colorTexture, ivec2(channel, 0), 0).a == 0.0)
        discard;

    float low;
    float high;
    if (!Bin(channel, p, low, high))
        discard;

    float value = abs(high) >= abs(low) ? high : low;
    float magnitude = abs(value);
    if (magnitude <= threshold)
    {
        fragmentColor = vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    int middle = colormapLength / 2;
    int level = int(min(1.0, magnitude / range) * float(middle - 1) + 0.5);
    int index = value >= 0.0 ? middle + level : middle - 1 - level;
    fragmentColor = texelFetch(colormapTexture, ivec2(index, 0), 0);
}
