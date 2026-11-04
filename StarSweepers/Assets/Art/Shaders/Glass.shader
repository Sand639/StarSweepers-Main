// ガラスの見た目を作るシェーダー（URP用）。
//
// ねらいは4つ。
//   1. 透けている
//   2. 向こう側がそのまま見える（ゆがませない）
//   3. **そこにガラスがあると分かる**
//   4. 当たり判定は普通に効く（それはシェーダーではなく Collider の仕事）
//
// 3つ目が肝で、ただ透明にすると**何も無いように見えて頭をぶつける。**
// そこで2つの手を使っている。
//
//   ふち（フレネル）… **正面から見ると透けて、斜めから見ると白くなる。**
//                      ガラスの端や角が浮かび上がるので、板の存在が分かる
//   つや（ハイライト）… 光を反射してキラッと光る。ガラスらしさが出る
//
// 板を厚みのある箱にすると、角のふちが二重に見えてさらに分かりやすい。
Shader "StarSweepers/Glass"
{
    Properties
    {
        [MainColor] _BaseColor("ガラスの色（Aで濃さ）", Color) = (0.75, 0.88, 0.95, 0.12)
        _EdgeColor("ふちの色", Color) = (1, 1, 1, 1)
        _EdgePower("ふちの細さ（大きいほど細い）", Range(0.5, 8)) = 3
        _EdgeStrength("ふちの強さ", Range(0, 1)) = 0.7
        _Shininess("つやの鋭さ", Range(0, 1)) = 0.85
        _SpecularStrength("つやの強さ", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // 透けさせるための決まりごと。
            // ZWrite Off にしないと、後ろにある物が描かれなくなる
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            // 裏からも見えるようにする（薄い板を裏から見ても消えない）
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EdgeColor;
                half _EdgePower;
                half _EdgeStrength;
                half _Shininess;
                half _SpecularStrength;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs position = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(IN.normalOS);

                OUT.positionHCS = position.positionCS;
                OUT.positionWS = position.positionWS;
                OUT.normalWS = normal.normalWS;

                return OUT;
            }

            half4 frag(Varyings IN, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                // 裏側を見ているときは、面の向きが逆になっているので直す
                float3 normalWS = normalize(IN.normalWS) * IS_FRONT_VFACE(facing, 1.0, -1.0);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // 面を正面から見ているほど0、斜めから見るほど1に近づく
                half fresnel = pow(saturate(1.0 - saturate(dot(normalWS, viewDirWS))), _EdgePower);
                half edge = fresnel * _EdgeStrength;

                // 光を反射したときの、キラッとした点
                Light mainLight = GetMainLight();
                float3 halfDirWS = normalize(mainLight.direction + viewDirWS);
                half specular =
                    pow(saturate(dot(normalWS, halfDirWS)), exp2(_Shininess * 9.0 + 1.0))
                    * _SpecularStrength;

                half3 color = _BaseColor.rgb + _EdgeColor.rgb * edge + mainLight.color * specular;

                // **ふちとつやの分だけ、そこだけ濃くなる**（真ん中は透けたまま）
                half alpha = saturate(_BaseColor.a + edge + specular);

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
