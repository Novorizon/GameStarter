Shader "GameStarter/Preview/Tidal Water"
{
    Properties
    {
        _Ocean ("Ocean Surface", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Ocean;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 flow : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float3 positionWS : TEXCOORD0;
                float2 flow : TEXCOORD1;
                float fog : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.color = input.color;
                output.flow = input.flow;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.positionWS.xz;
                float time = _Time.y;
                float first = sin(dot(p, float2(.93, 1.54)) + 1.5 * sin(p.x * .38 - p.y * .31) + time * .95);
                float second = sin(dot(p, float2(1.51, -.67)) + sin(p.y * .59) - time * .74);
                float ripple = pow(saturate(1 - abs(first - second) * 1.95), 5);
                float shore = input.flow.x - .55 - .32 * sin(input.flow.y * 1.12 + time * 1.8);
                float foam = 1 - smoothstep(.075, .32, abs(shore));
                foam += .32 * (1 - smoothstep(.09, .31, abs(shore - 1.36)));
                float waterfall = pow(saturate(.5 + .5 * sin(input.flow.y * 3.1 + input.flow.x * 16 - time * 5)), 3);
                float shoreSheen = lerp(.55, 1, saturate(1 - input.flow.x / 45));
                float glint = lerp(.10 * ripple + .5 * waterfall * input.color.a, .26 * ripple * shoreSheen + .8 * foam, _Ocean);
                half3 color = lerp(input.color.rgb * (.91 + .09 * first), half3(.84, .96, 1), saturate(glint));
                return half4(MixFog(color, input.fog), 1);
            }
            ENDHLSL
        }
    }
}
