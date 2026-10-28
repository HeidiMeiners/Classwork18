using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Ponlo en cada mano (LeftHand Controller / RightHand Controller del XR Origin).
/// Con el botón de agarre (grip) agarras el Rigidbody más cercano y al soltarlo
/// sale lanzado con la velocidad lineal y angular que llevaba tu mano.
/// </summary>
public class VRGrabber : MonoBehaviour
{
    [Header("Mano")]
    public XRNode hand = XRNode.RightHand;

    [Header("Agarre")]
    [Tooltip("Radio (en metros) alrededor de la mano donde se puede agarrar.")]
    public float grabRadius = 0.12f;
    [Tooltip("Capas de los objetos que se pueden agarrar.")]
    public LayerMask grabbableLayers = ~0;

    [Header("Lanzamiento")]
    public float throwVelocityMultiplier = 1.5f;
    public float throwAngularMultiplier = 1f;
    [Tooltip("Cuántos frames recientes se usan para calcular la velocidad al soltar.")]
    [Range(3, 15)] public int sampleCount = 6;

    struct Sample
    {
        public Vector3 pos;
        public Quaternion rot;
        public float time;
    }

    readonly Queue<Sample> samples = new Queue<Sample>();

    Rigidbody held;
    Vector3 localPosOffset;
    Quaternion localRotOffset;
    bool wasGripping;

    void Update()
    {
        RecordSample();

        bool gripping = IsGripping();

        if (gripping && !wasGripping && held == null)
            TryGrab();
        else if (!gripping && wasGripping && held != null)
            Release();

        wasGripping = gripping;
    }

    void LateUpdate()
    {
        // Mientras lo sostienes, el cubo sigue a la mano.
        if (held != null)
        {
            held.transform.SetPositionAndRotation(
                transform.TransformPoint(localPosOffset),
                transform.rotation * localRotOffset);
        }
    }

    bool IsGripping()
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(hand);
        return device.isValid
            && device.TryGetFeatureValue(CommonUsages.gripButton, out bool pressed)
            && pressed;
    }

    void RecordSample()
    {
        samples.Enqueue(new Sample
        {
            pos = transform.position,
            rot = transform.rotation,
            time = Time.time
        });

        while (samples.Count > sampleCount)
            samples.Dequeue();
    }

    void TryGrab()
    {
        Collider[] hits = Physics.OverlapSphere(
            transform.position, grabRadius, grabbableLayers, QueryTriggerInteraction.Ignore);

        Rigidbody closest = null;
        float best = float.MaxValue;

        foreach (Collider col in hits)
        {
            Rigidbody rb = col.attachedRigidbody;
            if (rb == null) continue;

            float d = (rb.position - transform.position).sqrMagnitude;
            if (d < best)
            {
                best = d;
                closest = rb;
            }
        }

        if (closest == null) return;

        held = closest;
        held.isKinematic = true;

        // Guarda la posición relativa a la mano para que no "salte" al agarrarlo.
        localPosOffset = transform.InverseTransformPoint(held.position);
        localRotOffset = Quaternion.Inverse(transform.rotation) * held.rotation;
    }

    void Release()
    {
        Rigidbody rb = held;
        held = null;

        rb.isKinematic = false;

        GetHandVelocities(out Vector3 linear, out Vector3 angular);

        // Velocidad del punto donde está el objeto (lineal de la mano + giro de la muñeca).
        Vector3 r = rb.position - transform.position;
        Vector3 velocity = (linear + Vector3.Cross(angular, r)) * throwVelocityMultiplier;

#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = velocity;
#else
        rb.velocity = velocity;
#endif
        rb.angularVelocity = angular * throwAngularMultiplier;
    }

    void GetHandVelocities(out Vector3 linear, out Vector3 angular)
    {
        linear = Vector3.zero;
        angular = Vector3.zero;

        if (samples.Count < 2) return;

        Sample first = default;
        Sample last = default;
        int i = 0;
        foreach (Sample s in samples)
        {
            if (i == 0) first = s;
            last = s;
            i++;
        }

        float dt = last.time - first.time;
        if (dt <= Mathf.Epsilon) return;

        linear = (last.pos - first.pos) / dt;

        Quaternion delta = last.rot * Quaternion.Inverse(first.rot);
        delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
        if (angleDeg > 180f) angleDeg -= 360f;

        if (!float.IsInfinity(axis.x) && !float.IsNaN(axis.x))
            angular = axis * (angleDeg * Mathf.Deg2Rad / dt);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, grabRadius);
    }
}