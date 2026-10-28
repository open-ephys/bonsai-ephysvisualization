// The envelope, channels by columns, one texture for the minima and one for the maxima.
uniform sampler2D minimaTexture;
uniform sampler2D maximaTexture;

// For each pixel column, the columns it takes its min and max from: those it combines, from x up to y, or where
// there are fewer columns than pixels, the two it interpolates between, z of the way from x to y. Worked out by
// the plot's pixel axis, so that what is drawn here agrees with everything placed over it.
uniform sampler2D cellTexture;
uniform int combines;

// Each channel's slot (its offset below the first row's top, in row heights), and the first row's slot.
uniform sampler2D slotTexture;
uniform float firstSlot;

float Slot(int channel)
{
    return texelFetch(slotTexture, ivec2(channel, 0), 0).r;
}

// The top of a slot in pixels below the texture's top, matching the plot's rows.
float SlotTop(float slot, float origin, float rowHeight)
{
    return origin + (slot - firstSlot) * rowHeight;
}

// The min and max channel shows at pixel column p, or false where the pixel holds no column or a column not yet
// written, which holds NaN.
bool Bin(int channel, int p, out float low, out float high)
{
    vec3 cell = texelFetch(cellTexture, ivec2(p, 0), 0).xyz;
    int first = int(cell.x);
    int last = int(cell.y);
    if (combines != 0)
    {
        low = 0.0;
        high = 0.0;
        if (last <= first)
            return false;

        low = texelFetch(minimaTexture, ivec2(first, channel), 0).r;
        high = texelFetch(maximaTexture, ivec2(first, channel), 0).r;
        for (int c = first + 1; c < last; c++)
        {
            low = min(low, texelFetch(minimaTexture, ivec2(c, channel), 0).r);
            high = max(high, texelFetch(maximaTexture, ivec2(c, channel), 0).r);
        }
        return !isnan(low) && !isnan(high);
    }

    float low0 = texelFetch(minimaTexture, ivec2(first, channel), 0).r;
    float low1 = texelFetch(minimaTexture, ivec2(last, channel), 0).r;
    float high0 = texelFetch(maximaTexture, ivec2(first, channel), 0).r;
    float high1 = texelFetch(maximaTexture, ivec2(last, channel), 0).r;
    low = low0 + cell.z * (low1 - low0);
    high = high0 + cell.z * (high1 - high0);
    return !isnan(low) && !isnan(high);
}
