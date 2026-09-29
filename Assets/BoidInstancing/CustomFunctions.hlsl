//UNITY_SHADER_NO_UPGRADE
#ifndef MYHLSLINCLUDE_INCLUDED
#define MYHLSLINCLUDE_INCLUDED


void GetSandColor_float(float BiomeID, out float4 Out)
{
    // Vertex colors are interpolated, so an ID of 1 can arrive as 0.99999994 on some GPUs. Round before comparing.
    int id = (int)round(BiomeID);
    if (id == 0) {
        Out = _SandColor;
    } else if (id == 1) {
        Out = _RedSandColor;
    } else if (id == 2) {
        Out = _GreenSandColor;
    } else if (id == 3) {
        Out = _OpenBiomeColor;
    } else {
        Out = _RockColor;
    }
}

#endif // MYHLSLINCLUDE_INCLUDED
