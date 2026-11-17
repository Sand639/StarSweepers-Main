// 計算で作る星空の背景（スカイボックス）。説明は Documents/機能ドキュメント/計算で作る星空の背景.md
// 画像を使わず、空のどの向きにどんな星があるかを、その場で計算して描く。
// 星ごとに「色（温度）・大きさ・明るさ・瞬きのリズム」を乱数で決める。色は現実の星と同じく 赤 → オレンジ → 黄 → 白 → 青白 → 青。
// 小さい星がたくさんの層と、明るい星が少しの層の2つを重ねている。
//
// しくみ：空の向き（長さ1の矢印）を、細かい立方体の升目の空間に置く。升目ごとに星を1つ置き、
// 球の表面の近く（厚さ1升）にある星だけを描く。まわり 3×3×3 の升目だけを調べれば、星が途中で欠けない。
// 画面の1ドットより小さい星は、大きさを1ドットに広げて、そのぶん暗くする（遠くの星がちらちら消えたり出たりしないように）。
Shader "StarSweepers/ProceduralStarrySky"
{
    Properties
    {
        _BackgroundColor ("Background (空の地の色)", Color) = (0.006, 0.008, 0.02, 1)
        _Density ("Density (小さい星の細かさ。大きいほど数が増える)", Range(10, 200)) = 90
        _BigDensity ("Big Star Density (明るい星の細かさ。大きいほど数が増える)", Range(4, 60)) = 20
        _StarSize ("Star Size (星の大きさ)", Range(0.01, 0.15)) = 0.05
        _Brightness ("Brightness (星の明るさ)", Range(0, 10)) = 2
        _BrightnessCurve ("Brightness Curve (大きいほど暗い星が増え、明るい星がまれになる)", Range(1, 12)) = 4
        _ColorAmount ("Color Amount (星の色の濃さ。0=白だけ、1=現実の星くらい、2以上=はっきり)", Range(0, 3)) = 1.6
        _Glow ("Glow (明るい星のまわりのにじみ)", Range(0, 1)) = 0.3
        _TwinkleAmount ("Twinkle Amount (瞬きの強さ。0で瞬かない)", Range(0, 1)) = 0.4
        _TwinkleSpeed ("Twinkle Speed (瞬きの速さ)", Range(0, 10)) = 2.5
        _Exposure ("Exposure (全体の明るさ)", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ProceduralStarrySky"

            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BackgroundColor;
                float _Density;
                float _BigDensity;
                float _StarSize;
                float _Brightness;
                float _BrightnessCurve;
                float _ColorAmount;
                float _Glow;
                float _TwinkleAmount;
                float _TwinkleSpeed;
                float _Exposure;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // 空の箱（球）は自分を中心に置かれるので、頂点の位置がそのまま空の向きになる
                output.direction = input.positionOS.xyz;
                return output;
            }

            // 升目の番号から、0〜1 の乱数を3つ作る（同じ升目なら毎回同じ値）
            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            // 星の温度（0〜1）から色を決める。0=赤（冷たい星）… 1=青（熱い星）
            float3 StarColor(float temperature)
            {
                const float3 red = float3(1.00, 0.45, 0.30);
                const float3 orange = float3(1.00, 0.68, 0.38);
                const float3 yellow = float3(1.00, 0.88, 0.62);
                const float3 white = float3(1.00, 1.00, 1.00);
                const float3 paleBlue = float3(0.72, 0.82, 1.00);
                const float3 blue = float3(0.50, 0.62, 1.00);

                float s = temperature * 5.0;
                float3 color = lerp(red, orange, saturate(s));
                color = lerp(color, yellow, saturate(s - 1.0));
                color = lerp(color, white, saturate(s - 2.0));
                color = lerp(color, paleBlue, saturate(s - 3.0));
                color = lerp(color, blue, saturate(s - 4.0));

                // 色の濃さ：白から離す量を変える（1より大きいと現実より鮮やか）。いちばん明るい成分を1にそろえて、明るさは変えない
                color = max(lerp(float3(1.0, 1.0, 1.0), color, _ColorAmount), 0.0);
                return color / max(max(color.r, color.g), max(color.b, 0.001));
            }

            // 星の層を1つ描く。density=升目の細かさ、sizeCells=星の大きさ（升目に対する割合）
            float3 StarLayer(float3 direction, float density, float sizeCells, float brightness, float pixelAngle, float seed)
            {
                float3 p = direction * density;
                float3 baseCell = floor(p);
                float3 sum = 0.0;

                [unroll] for (int z = -1; z <= 1; z++)
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float3 cell = baseCell + float3(x, y, z);
                    float3 h1 = Hash33(cell + seed);
                    float3 center = cell + h1; // 升目の中のどこかに星を置く
                    float len = length(center);

                    // 球の表面の近く（厚さ1升）の星だけ使う。これより遠い星は、まわりの升目を調べても拾いきれず欠けるため
                    if (abs(len - density) > 0.5)
                    {
                        continue;
                    }

                    float3 h2 = Hash33(cell + seed + 19.19);
                    float3 h3 = Hash33(cell + seed + 47.73);

                    float dist = length(direction - center / len); // 星までの向きの差（ほぼ角度。ラジアン）
                    float size = sizeCells * (0.5 + h2.x) / density;

                    // 1ドットより小さい星は1ドットに広げ、面積が増えたぶん暗くする
                    float drawSize = max(size, pixelAngle * 0.8);
                    float energy = (size * size) / (drawSize * drawSize);
                    float r = dist / drawSize;
                    float shape = exp(-r * r * 2.0) + _Glow * exp(-r);

                    float twinkle = 1.0 + _TwinkleAmount * (
                        0.6 * sin(_Time.y * _TwinkleSpeed * (0.7 + h2.z) + h1.x * 50.0) +
                        0.4 * sin(_Time.y * _TwinkleSpeed * 2.3 * (0.6 + h1.y) + h1.z * 80.0));

                    float starBrightness = brightness * pow(h2.y, _BrightnessCurve) * energy * max(twinkle, 0.0);
                    sum += StarColor(h3.x) * starBrightness * shape;
                }

                return sum;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float pixelAngle = length(fwidth(direction)); // 画面の1ドットが空でどれくらいの角度か

                float3 color = _BackgroundColor.rgb;
                color += StarLayer(direction, _Density, _StarSize, _Brightness * 0.6, pixelAngle, 0.0);
                color += StarLayer(direction, _BigDensity, _StarSize * 0.8, _Brightness * 2.5, pixelAngle, 113.0);

                return half4(color * _Exposure, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
