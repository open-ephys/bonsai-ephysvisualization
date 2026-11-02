// Reads a channel's tile for each pixel of the view, at the value the pixel shows under the view's range, so a
// change of range only reads the same counts differently.
uniform sampler2D atlas;
uniform vec2 atlasSize;
uniform vec2 tile;
uniform vec2 tileSize;
uniform vec2 viewSize;
uniform float center;
uniform float range;
uniform float spikes;
uniform vec4 color;
out vec4 fragment;

void main()
{
    // NB: the texture's first row is the image's top, as it is shown.
    float x = clamp(gl_FragCoord.x / viewSize.x * tileSize.x, 0.5, tileSize.x - 0.5);
    float value = center + range * (0.5 - gl_FragCoord.y / viewSize.y);
    float y = binOf(value);
    float count = texture(atlas, (tile + vec2(x, y)) / atlasSize).r;

    // NB: on a log scale against the most any pixel can hold, the channel's spike count up to what the atlas counts
    // to, so a rare unit still shows beside a busy one and the brightest keeps its brightness as spikes go on
    // arriving.
    float brightness = spikes > 0.0 ? clamp(log(1.0 + count) / log(1.0 + spikes), 0.0, 1.0) : 0.0;
    fragment = vec4(color.rgb, brightness);
}
