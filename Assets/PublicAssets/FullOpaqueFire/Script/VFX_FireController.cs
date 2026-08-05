using UnityEngine;

namespace YourNamespace
{
    [ExecuteAlways]
    public class VFX_FireController : MonoBehaviour
    {
        [Header("R?lages VFX Feu")]
        [SerializeField] private Color fireColor = Color.red;
        [SerializeField, Range(0f, 2f)] private float fireIntensity = 1f;
        [SerializeField] private Vector3 fireWindDirection = Vector3.zero;

        private ParticleSystem[] fireParticleSystems;
        private float[] defaultFireRateValues;        // Valeurs par d?aut du spawn rate (Emission)
        private float[] defaultFireStartSizeValues;     // Valeurs par d?aut de la taille (Main > startSize)
        private Light fireLight; // Adding reference to Light component for intensity control

        private void Awake()
        {
            FindFireParticles();
            ApplyFireSettings();
        }

        private void OnValidate()
        {
            // En mode ?iteur, il se peut qu'Awake() ne soit pas appel?
            // donc on s'assure que les tableaux sont initialis?.
            if (fireParticleSystems == null || fireParticleSystems.Length == 0 ||
                defaultFireRateValues == null || defaultFireStartSizeValues == null ||
                defaultFireRateValues.Length != fireParticleSystems.Length ||
                defaultFireStartSizeValues.Length != fireParticleSystems.Length)
            {
                FindFireParticles();
            }
            ApplyFireSettings();
        }

        /// <summary>
        /// Recherche tous les ParticleSystem enfants et sauvegarde leurs valeurs par d?aut.
        /// Le spawn rate est dans le module Emission et la taille dans le module Main (startSize).
        /// </summary>
        private void FindFireParticles()
        {
            fireParticleSystems = GetComponentsInChildren<ParticleSystem>();
            int count = fireParticleSystems.Length;
            defaultFireRateValues = new float[count];
            defaultFireStartSizeValues = new float[count];

            for (int i = 0; i < count; i++)
            {
                ParticleSystem ps = fireParticleSystems[i];
                if (ps != null)
                {
                    var mainModule = ps.main;
                    var emissionModule = ps.emission;
                    defaultFireRateValues[i] = emissionModule.rateOverTime.constant;
                    defaultFireStartSizeValues[i] = mainModule.startSize.constant;
                }
            }

            fireLight = GetComponentInChildren<Light>(); // Retrieve the Light component if present
        }

        /// <summary>
        /// Applique les r?lages sur chaque ParticleSystem enfant.
        /// Les valeurs par d?aut sont multipli?s par fireIntensity, exactement comme pour le spawn rate.
        /// </summary>
        private void ApplyFireSettings()
        {
            // S'assurer que tous les tableaux sont initialis? correctement.
            if (fireParticleSystems == null || fireParticleSystems.Length == 0 ||
                defaultFireRateValues == null || defaultFireStartSizeValues == null ||
                defaultFireRateValues.Length != fireParticleSystems.Length ||
                defaultFireStartSizeValues.Length != fireParticleSystems.Length)
            {
                FindFireParticles();
            }

            for (int i = 0; i < fireParticleSystems.Length; i++)
            {
                ParticleSystem ps = fireParticleSystems[i];
                if (ps == null)
                    continue;

                var mainModule = ps.main;
                var emissionModule = ps.emission;
                var velocityModule = ps.velocityOverLifetime;

                // Appliquer la couleur
                mainModule.startColor = fireColor;

                // Modifier le spawn rate en multipliant la valeur par d?aut par fireIntensity
                float baseRate = defaultFireRateValues[i];
                if (emissionModule.rateOverTime.mode == ParticleSystemCurveMode.Constant)
                {
                    emissionModule.rateOverTime = new ParticleSystem.MinMaxCurve(baseRate * fireIntensity);
                }
                else
                {
                    emissionModule.rateOverTime = new ParticleSystem.MinMaxCurve(baseRate * fireIntensity, baseRate * fireIntensity);
                }

                // Modifier la taille des particules de la m?e mani?e
                float baseSize = defaultFireStartSizeValues[i];
                mainModule.startSize = new ParticleSystem.MinMaxCurve(baseSize * fireIntensity);

                // Appliquer la direction du vent si le module Velocity over Lifetime est activ?
                if (velocityModule.enabled)
                {
                    velocityModule.xMultiplier = fireWindDirection.x;
                    velocityModule.yMultiplier = fireWindDirection.y;
                    velocityModule.zMultiplier = fireWindDirection.z;
                }
            }

            // Apply fire light intensity based on fire intensity
            if (fireLight != null)
            {
                fireLight.intensity = fireIntensity;
                fireLight.color = fireColor;
            }
        }

        public void SetFireColor(Color newColor)
        {
            fireColor = newColor;
            ApplyFireSettings();
        }

        public void SetFireIntensity(float newIntensity)
        {
            fireIntensity = Mathf.Clamp(newIntensity, 0f, 4f);
            ApplyFireSettings();
        }

        public void EnableCollision(LayerMask collisionLayer)
        {
            for (int i = 0; i < fireParticleSystems.Length; i++)
            {
                ParticleSystem ps = fireParticleSystems[i];
                if (ps != null)
                {
                    var collision = ps.collision;
                    collision.enabled = true;
                    // World 충돌 모드 ?��?
                    collision.type = ParticleSystemCollisionType.World;
                    collision.mode = ParticleSystemCollisionMode.Collision3D;
                    collision.collidesWith = collisionLayer;
                    
                    // ?�비 ?�림 방�?�??�해 충돌 검???�질 ?�임
                    collision.quality = ParticleSystemCollisionQuality.High;
                    
                    // ?�티???�기가 커졌?????�각?�으�?벽을 ?�과?�는 ?�상??막기 ?�해
                    // 충돌 구체???�기�??�제 ?�프?�이???�기??가깝게(0.9) ?�정?�니??
                    collision.radiusScale = 0.9f; 
                    
                    // [?�피기동 ?�출] 
                    // bounceMultiplier가 ?�을?�록 ?�비???�으�??? ?�고 ?�깁?�다.
                    // dampenMultiplier??충돌 ???�도 감소??(0??가까울?�록 ?�도 ?��?, 1?�면 벽에 붙음)
                    collision.bounceMultiplier = 0.4f;
                    collision.dampenMultiplier = 0.3f;
                }
            }
        }

        public void SetFireWindDirection(Vector3 newWindDirection)
        {
            fireWindDirection = newWindDirection;
            ApplyFireSettings();
        }

        public Color GetFireColor() { return fireColor; }
        public float GetFireIntensity() { return fireIntensity; }
        public Vector3 GetFireWindDirection() { return fireWindDirection; }
    }
}
