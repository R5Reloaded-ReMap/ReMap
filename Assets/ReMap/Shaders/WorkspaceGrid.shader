Shader "ReMap/WorkspaceGrid"
{
    Properties { _GridStep("Grid step",Float)=1.6256 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-20" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _GridStep;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; };
            Varyings vert(Attributes input)
            {
                Varyings o;
                o.world = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.world);
                return o;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float2 coord = input.world.xz / max(_GridStep,.0001);
                float2 deriv = max(fwidth(coord), .0001);
                float2 grid = abs(frac(coord - .5) - .5) / deriv;
                float gridLine = 1 - saturate(min(grid.x, grid.y));
                float fade = 1 - smoothstep(40, 130, distance(_WorldSpaceCameraPos, input.world));
                half3 color = half3(.06,.085,.115);
                float axisX = 1 - saturate(abs(coord.y) / deriv.y);
                float axisZ = 1 - saturate(abs(coord.x) / deriv.x);
                color = lerp(color, half3(.48,.22,.25), axisX * fade * .7);
                color = lerp(color, half3(.18,.45,.25), axisZ * fade * .7);
                float alpha = lerp(.035, .34, gridLine * fade);
                alpha = max(alpha, (axisX + axisZ) * fade * .48);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
