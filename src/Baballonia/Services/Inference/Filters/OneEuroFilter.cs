using System;
using System.Collections.Generic;
using System.Linq;

namespace Baballonia.Services.Inference.Filters;

public class OneEuroFilter : IFilter
{
    private float[] minCutoff;
    private float[] beta;
    private float[] dCutoff;
    private float[] xPrev;
    private float[] dxPrev;
    private DateTime tPrev;
    public OneEuroFilter(float[] x0, float minCutoff = 1.0f, float beta = 0.0f)
    {
        float dx0 = 0.0f;
        float dCutoff = 1.0f;
        int length = x0.Length;
        this.minCutoff = CreateFilledArray(length, minCutoff);
        this.beta = CreateFilledArray(length, beta);
        this.dCutoff = CreateFilledArray(length, dCutoff);
        this.xPrev = (float[])x0.Clone();
        this.dxPrev = CreateFilledArray(length, dx0);
        this.tPrev = DateTime.UtcNow;
    }

    public float[] Filter(float[] x)
    {
        if (x.Length != xPrev.Length)
            throw new ArgumentException($"Input shape does not match initial shape. Expected: {xPrev.Length}, got: {x.Length}");

        DateTime now = DateTime.UtcNow;
        float elapsedTime = (float)(now - tPrev).TotalSeconds;

        if (elapsedTime == 0.0f)
        {
            xPrev = (float[])x.Clone();
            return x;
        }

        float[] t_e = CreateFilledArray(x.Length, elapsedTime);

        float[] dx = new float[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            dx[i] = (x[i] - xPrev[i]) / t_e[i];
        }

        float[] a_d = SmoothingFactor(t_e, dCutoff);
        float[] dxHat = ExponentialSmoothing(a_d, dx, dxPrev);

        float[] cutoff = new float[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            cutoff[i] = minCutoff[i] + beta[i] * Math.Abs(dxHat[i]);
        }

        float[] a = SmoothingFactor(t_e, cutoff);
        float[] xHat = ExponentialSmoothing(a, x, xPrev);

        xPrev = xHat;
        dxPrev = dxHat;
        tPrev = now;

        return xHat;
    }

    private float[] CreateFilledArray(int length, float value)
    {
        float[] arr = new float[length];
        for (int i = 0; i < length; i++) arr[i] = value;
        return arr;
    }

    private float[] SmoothingFactor(float[] t_e, float[] cutoff)
    {
        int length = t_e.Length;
        float[] result = new float[length];
        for (int i = 0; i < length; i++)
        {
            float r = 2 * (float)Math.PI * cutoff[i] * t_e[i];
            result[i] = r / (r + 1);
        }
        return result;
    }

    private float[] ExponentialSmoothing(float[] a, float[] x, float[] xPrev)
    {
        int length = a.Length;
        float[] result = new float[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = a[i] * x[i] + (1 - a[i]) * xPrev[i];
        }
        return result;
    }
}

public class ParameterGroupFilter : IFilter
{
    private readonly Dictionary<string, OneEuroFilter> _groupFilters;
    private readonly Dictionary<int, string> _parameterIndexToGroup;
    
    public ParameterGroupFilter()
    {
        _groupFilters = new Dictionary<string, OneEuroFilter>();
        _parameterIndexToGroup = new Dictionary<int, string>();
    }

    public void ConfigureGroupFilter(string groupName, int[] parameterIndices, float minCutoff, float beta)
    {
        float[] dummyArray = new float[parameterIndices.Length];
        var filter = new OneEuroFilter(dummyArray, minCutoff, beta);
        
        _groupFilters[groupName] = filter;
        
        for (int i = 0; i < parameterIndices.Length; i++)
        {
            _parameterIndexToGroup[parameterIndices[i]] = groupName;
        }
    }

    public void DisableGroupFilter(string groupName)
    {
        _groupFilters.Remove(groupName);
        
        var keysToRemove = _parameterIndexToGroup.Where(kvp => kvp.Value == groupName).Select(kvp => kvp.Key).ToArray();
        foreach (var key in keysToRemove)
        {
            _parameterIndexToGroup.Remove(key);
        }
    }

    public float[] Filter(float[] input)
    {
        if (!_groupFilters.Any())
        {
            return input;
        }

        float[] result = (float[])input.Clone();
        
        var groupedParameters = new Dictionary<string, List<(int index, float value)>>();
        
        for (int i = 0; i < input.Length; i++)
        {
            if (_parameterIndexToGroup.TryGetValue(i, out string? groupName))
            {
                if (!groupedParameters.ContainsKey(groupName))
                {
                    groupedParameters[groupName] = new List<(int, float)>();
                }
                groupedParameters[groupName].Add((i, input[i]));
            }
        }

        foreach (var group in groupedParameters)
        {
            string groupName = group.Key;
            var parameters = group.Value;
            
            if (_groupFilters.TryGetValue(groupName, out OneEuroFilter? filter))
            {
                float[] groupValues = parameters.Select(p => p.value).ToArray();
                float[] filteredValues = filter.Filter(groupValues);
                
                for (int i = 0; i < parameters.Count; i++)
                {
                    result[parameters[i].index] = filteredValues[i];
                }
            }
        }

        return result;
    }
}
