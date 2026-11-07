Shader "Scheherazade/Conveyor/Color"
{
    Properties { _BaseColor ("Color", Color) = (0.3, 0.8, 0.7, 1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
            #include "UnityCG.cginc"
            UNITY_INSTANCING_BUFFER_START(GrainProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
            UNITY_INSTANCING_BUFFER_END(GrainProperties)
            struct Attributes { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 position : SV_POSITION; float light : TEXCOORD0; float4 color : COLOR0; };
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.color = UNITY_ACCESS_INSTANCED_PROP(GrainProperties, _BaseColor);
                float3 normal = UnityObjectToWorldNormal(input.normal);
                output.light = 0.45 + 0.55 * saturate(dot(normal, normalize(float3(-0.4, 1, 0.6))));
                return output;
            }
            float4 Frag(Varyings input) : SV_Target { return float4(input.color.rgb * input.light, input.color.a); }
        ENDHLSL
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="Always" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
}
