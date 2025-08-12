using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Avalonia.Animation.Easings;

namespace Ecliptix.Core.Controls.Modals.BottomSheetModal;

public sealed class SpringEasing : Easing
{
    private static readonly FrozenDictionary<int, double> PrecomputedValues;
    private const int LUT_SIZE = 1000;
    private const double INV_LUT_SIZE = 1.0 / LUT_SIZE;
    
    static SpringEasing()
    {
        Dictionary<int, double> values = new(LUT_SIZE + 1);
        const double damping = 1.0; // Smoother damping
        const double stiffness = 0.88; // Higher stiffness for smoothness
        
        for (int i = 0; i <= LUT_SIZE; i++)
        {
            double t = i * INV_LUT_SIZE;
            if (t <= 0) 
            {
                values[i] = 0;
                continue;
            }
            if (t >= 1)
            {
                values[i] = 1;
                continue;
            }
            
            // Ultra-smooth calculation with refined parameters
            double expApprox = FastExp(-damping * t * 0.9); // Gentler damping
            double cosApprox = FastCos((1 - stiffness) * Math.PI * t * 5.5); // Smoother oscillation
            values[i] = 1 - expApprox * (1 - t) * cosApprox * 0.95; // Reduced intensity
        }
        
        PrecomputedValues = values.ToFrozenDictionary();
    }

    public override double Ease(double progress)
    {
        if (progress <= 0) return 0;
        if (progress >= 1) return 1;

        // Use lookup table for maximum performance
        int index = (int)(progress * LUT_SIZE);
        return PrecomputedValues.GetValueOrDefault(index, progress);
    }
    
    private static double FastExp(double x)
    {
        // Fast exp approximation using polynomial (Pade approximant)
        if (x < -10) return 0;
        if (x > 10) return 22026.4658;
        
        double x2 = x * x;
        return (1 + x + x2 * 0.5 + x2 * x * 0.16666667) / 
               (1 - x + x2 * 0.5 - x2 * x * 0.16666667);
    }
    
    private static double FastCos(double x)
    {
        // Fast cosine approximation using Chebyshev polynomial
        x = x % (2 * Math.PI);
        if (x < 0) x += 2 * Math.PI;
        
        if (x > Math.PI) x = 2 * Math.PI - x;
        
        double x2 = x * x;
        return 1 - x2 * 0.5 + x2 * x2 * 0.041666667 - x2 * x2 * x2 * 0.001388889;
    }
}