Shader "Game/VFX/Forest Outpost"
{
    Properties
    {
        _MainTex ("VFX Atlas", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 position : POSITION;
                half4 color : COLOR;
                float3 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                half4 color : COLOR;
                float3 uv : TEXCOORD0;
            };

            sampler2D _MainTex;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.position);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 sample = tex2D(_MainTex, input.uv.xy) * input.color;
                half emission = input.uv.z;
                half3 light = sample.rgb * (sample.a * (1 + emission * 0.55h));
                return half4(light, sample.a * (1 - emission));
            }
            ENDCG
        }
    }
}
