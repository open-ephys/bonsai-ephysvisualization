// One end of a waveform's line segment: its x in the atlas's pixels, its channel's tile's first row, and the
// sample's value, which is turned into a row here.
layout(location = 0) in vec3 point;
uniform vec2 atlasSize;

void main()
{
    vec2 pixel = vec2(point.x, point.y + binOf(point.z));
    gl_Position = vec4(pixel / atlasSize * 2.0 - 1.0, 0.0, 1.0);
}
