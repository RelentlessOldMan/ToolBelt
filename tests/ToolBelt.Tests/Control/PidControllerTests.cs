using System;
using ToolBelt.Control;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Control
{
    public sealed class PidControllerTests
    {
        public void ConvergesToSetpointOnFirstOrderPlant()
        {
            // Plant: dy/dt = -y + u (first order). PID with integral action should reach the setpoint.
            var pid = new PidController(kp: 2, ki: 1, kd: 0.1);
            pid.Setpoint = 5;
            double y = 0, dt = 0.01;
            for (int step = 0; step < 5000; step++)
            {
                double u = pid.Update(y, dt);
                y += dt * (-y + u);
            }
            Check.Close(5, y, 0.05); // steady-state error eliminated by the integral term
        }

        public void OutputIsClamped()
        {
            var pid = new PidController(kp: 100, ki: 0, kd: 0, outputMin: -1, outputMax: 1);
            pid.Setpoint = 1000;
            double u = pid.Update(0, 0.1);
            Check.True(u <= 1 && u >= -1, $"output {u} not clamped");
            Check.Equal(1.0, u); // huge error saturates high
        }

        public void ResetClearsState()
        {
            var pid = new PidController(kp: 1, ki: 1, kd: 0);
            pid.Setpoint = 10;
            for (int i = 0; i < 10; i++) pid.Update(0, 0.1); // build up integral
            pid.Reset();
            double u = pid.Update(10, 0.1); // at setpoint, no error -> ~0 with cleared integral
            Check.Close(0, u, 1e-9);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => new PidController(1, 1, 1, outputMin: 5, outputMax: 1));
            var pid = new PidController(1, 0, 0);
            Check.Throws<ArgumentOutOfRangeException>(() => pid.Update(0, 0));
        }
    }
}
