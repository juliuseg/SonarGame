//UNITY_SHADER_NO_UPGRADE
#ifndef MYHLSLINCLUDE_INCLUDED
#define MYHLSLINCLUDE_INCLUDED


void GetSandColor_float(float BiomeID, out float4 Out)
{
    if (BiomeID == 0) {
        Out = _SandColor;
    } else if (BiomeID == 1) {
        Out = _RedSandColor;
    } else if (BiomeID == 2) {
        Out = _GreenSandColor;
    } else if (BiomeID == 3) {
        Out = _OpenBiomeColor;
    } else {
        Out = _RockColor;
    }
}

#endif // MYHLSLINCLUDE_INCLUDED
