namespace Pixelbadger.Toolkit.Services;

/// <summary>
/// AdamW optimizer (decoupled weight decay) over a fixed set of parameter tensors.
/// </summary>
public sealed class AdamW
{
    private readonly IReadOnlyList<Tensor> _parameters;
    private readonly float _lr;
    private readonly float _beta1;
    private readonly float _beta2;
    private readonly float _eps;
    private readonly float _weightDecay;

    private readonly float[][] _m;
    private readonly float[][] _v;
    private int _step;

    public AdamW(
        IReadOnlyList<Tensor> parameters,
        float lr = 3e-4f,
        float beta1 = 0.9f,
        float beta2 = 0.95f,
        float eps = 1e-8f,
        float weightDecay = 0.1f)
    {
        _parameters = parameters;
        _lr = lr;
        _beta1 = beta1;
        _beta2 = beta2;
        _eps = eps;
        _weightDecay = weightDecay;

        _m = parameters.Select(p => new float[p.Length]).ToArray();
        _v = parameters.Select(p => new float[p.Length]).ToArray();
    }

    public void Step()
    {
        _step++;
        float biasCorr1 = 1f - MathF.Pow(_beta1, _step);
        float biasCorr2 = 1f - MathF.Pow(_beta2, _step);

        for (int pi = 0; pi < _parameters.Count; pi++)
        {
            var p = _parameters[pi];
            var m = _m[pi];
            var v = _v[pi];

            for (int i = 0; i < p.Length; i++)
            {
                float g = p.Grad[i];
                m[i] = _beta1 * m[i] + (1f - _beta1) * g;
                v[i] = _beta2 * v[i] + (1f - _beta2) * g * g;

                float mHat = m[i] / biasCorr1;
                float vHat = v[i] / biasCorr2;

                // Decoupled weight decay followed by the Adam step.
                p.Data[i] -= _lr * (_weightDecay * p.Data[i] + mHat / (MathF.Sqrt(vHat) + _eps));
            }
        }
    }
}
