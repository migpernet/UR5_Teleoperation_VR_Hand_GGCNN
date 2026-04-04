using UnityEngine;

public class OneEuroFilterVector3
{
    private float minCutoff;
    private float beta;
    private float dCutoff = 1.0f;
    private Vector3 xPrev;
    private Vector3 dxPrev;
    private bool firstTime = true;

    public OneEuroFilterVector3(float minCutoff = 1.0f, float beta = 0.0f)
    {
        this.minCutoff = minCutoff;
        this.beta = beta;
    }

    public Vector3 Filter(Vector3 x, float dt)
    {
        if (firstTime)
        {
            firstTime = false;
            xPrev = x;
            dxPrev = Vector3.zero;
            return x;
        }

        float rate = 1.0f / dt;
        Vector3 dx = (x - xPrev) * rate;
        float edx = Alpha(dCutoff, rate);
        dxPrev = dxPrev + edx * (dx - dxPrev);

        float velocity = dxPrev.magnitude;
        float cutoff = minCutoff + beta * velocity;
        float alpha = Alpha(cutoff, rate);

        xPrev = xPrev + alpha * (x - xPrev);
        return xPrev;
    }

    private float Alpha(float cutoff, float rate)
    {
        float tau = 1.0f / (2.0f * Mathf.PI * cutoff);
        return 1.0f / (1.0f + tau * rate);
    }
}

// Adaptação científica do 1 Euro para Rotações usando Slerp
public class OneEuroFilterQuaternion
{
    private float minCutoff;
    private float beta;
    private Quaternion qPrev;
    private float dCutoff = 1.0f;
    private float dqPrev;
    private bool firstTime = true;

    public OneEuroFilterQuaternion(float minCutoff = 1.0f, float beta = 0.0f)
    {
        this.minCutoff = minCutoff;
        this.beta = beta;
    }

    public Quaternion Filter(Quaternion q, float dt)
    {
        if (firstTime)
        {
            firstTime = false;
            qPrev = q;
            dqPrev = 0f;
            return q;
        }

        float rate = 1.0f / dt;
        float angularVelocity = Quaternion.Angle(qPrev, q) * rate;
        
        float edx = Alpha(dCutoff, rate);
        dqPrev = dqPrev + edx * (angularVelocity - dqPrev);

        float cutoff = minCutoff + beta * Mathf.Abs(dqPrev);
        float alpha = Alpha(cutoff, rate);

        qPrev = Quaternion.Slerp(qPrev, q, alpha);
        return qPrev;
    }

    private float Alpha(float cutoff, float rate)
    {
        float tau = 1.0f / (2.0f * Mathf.PI * cutoff);
        return 1.0f / (1.0f + tau * rate);
    }
}
