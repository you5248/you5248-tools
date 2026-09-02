// ノーマルマップを Blit で正しく展開して RGB エンコード法線として出力する補助シェーダー。
// ランタイムの DXT5nm / ASTC エンコードに関係なく UnpackNormal が正しい接空間法線を返すので、
// それを (n*0.5+0.5) で RGB に戻して読み出す。MeshArrayCombiner の法線焼き込み用。
Shader "Hidden/you5248/UnpackNormalBlit"
{
    Properties { _MainTex ("Normal", 2D) = "bump" {} }
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex   vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 frag(v2f_img i) : SV_Target
            {
                float3 n = UnpackNormal(tex2D(_MainTex, i.uv));
                return float4(n * 0.5 + 0.5, 1);
            }
            ENDCG
        }
    }
}
