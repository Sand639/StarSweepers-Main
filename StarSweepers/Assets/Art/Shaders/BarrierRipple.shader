// ステージを囲むバリア（StageBarrier）に物が当たったときの波紋。説明は Documents/機能ドキュメント/ステージを囲むバリア.md
// 板（Quad）の真ん中から輪が広がる。輪の先頭が黄緑、その内側が水色、さらに内側が黒のグラデーション。波は何重かに続き、広がりながら薄くなって消える。
// _Progress（0〜1）は、スクリプトが波紋ごとに MaterialPropertyBlock で渡す。
Shader "StarSweepers/BarrierRipple"
{
    Properties
    {
        _ColorFront ("Color Front (先頭の色・黄緑)", Color) = (0.65, 1.0, 0.2, 1)
        _ColorMiddle ("Color Middle (まん中の色・水色)", Color) = (0.2, 0.85, 1.0, 1)
        _ColorBack ("Color Back (うしろの色・黒)", Color) = (0.0, 0.0, 0.0, 1)
        _Progress ("Progress (進み具合 0-1)", Range(0, 1)) = 0.4
        _BandWidth ("Band Width (輪の太さ。半径に対する割合)", Range(0.05, 1)) = 0.22
        _Rings ("Rings (波の数)", Range(1, 4)) = 3
        _RingGap ("Ring Gap (波の間隔。半径に対する割合)", Range(0.05, 0.5)) = 0.26
        _MiddleAt ("Middle At (帯の中で水色になる位置 0-1)", Range(0.05, 0.95)) = 0.4
        _Intensity ("Intensity (濃さ)", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "BarrierRipple"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorFront;
                half4 _ColorMiddle;
                half4 _ColorBack;
                float _Progress;
                float _BandWidth;
                float _Rings;
                float _RingGap;
                float _MiddleAt;
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 真ん中からの距離（0＝当たった所、1＝板のふち）
                float r = length(input.uv * 2.0 - 1.0);
                float p = saturate(_Progress);

                // 先頭の輪の半径。はじめ速く、だんだんゆっくり広がる
                float front = 1.0 - (1.0 - p) * (1.0 - p);

                half3 color = 0;
                float alpha = 0;

                [unroll]
                for (int k = 0; k < 4; k++)
                {
                    if (k >= (int)_Rings)
                    {
                        break;
                    }

                    // d：この輪の帯の中のどこか（0＝先頭、1＝帯の終わり）
                    float d = (front - k * _RingGap - r) / _BandWidth;
                    if (d < 0.0 || d > 1.0)
                    {
                        continue;
                    }

                    half3 c = d < _MiddleAt
                        ? lerp(_ColorFront.rgb, _ColorMiddle.rgb, d / _MiddleAt)
                        : lerp(_ColorMiddle.rgb, _ColorBack.rgb, (d - _MiddleAt) / (1.0 - _MiddleAt));

                    // 先頭はくっきり、帯の終わり（黒）の手前までは濃いまま、最後にすっと消える。うしろの波ほど薄い
                    float a = smoothstep(0.0, 0.05, d) * (1.0 - d * d * d * d) / (1.0 + k * 0.6);
                    if (a > alpha)
                    {
                        alpha = a;
                        color = c;
                    }
                }

                // 広がるにつれて薄くし、板のふちで消す
                alpha *= (1.0 - p) * _Intensity * (1.0 - smoothstep(0.85, 1.0, r));
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
