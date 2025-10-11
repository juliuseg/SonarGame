//UNITY_SHADER_NO_UPGRADE
#ifndef MYHLSLINCLUDE_INCLUDED
#define MYHLSLINCLUDE_INCLUDED


void GetSandColor_float(float BiomeID, out float4 Out)
{
    if (BiomeID == 0) {
        Out = _SandColor;
    } else if (BiomeID == 1) {
        Out = _RedSandColor;
    } else {
        Out = _GreenSandColor;
    }
}

#endif // MYHLSLINCLUDE_INCLUDED
