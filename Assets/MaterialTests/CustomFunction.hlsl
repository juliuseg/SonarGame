#ifndef MYHLSLINCLUDE_INCLUDED
#define MYHLSLINCLUDE_INCLUDED

#ifdef _USE_ADDNUMBER    // <— guard starts here
StructuredBuffer<float3> _AddNumber;
#endif

void AddOne_float(uint In, out float3 Out)
{
    #ifdef _USE_ADDNUMBER
        // uint idx = (uint)In;
        Out = _AddNumber[In];
    #else
        Out = float3(0, 0, 0); // fallback if no buffer bound
    #endif
}

#endif // MYHLSLINCLUDE_INCLUDED
