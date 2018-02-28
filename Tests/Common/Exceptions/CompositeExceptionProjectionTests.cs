﻿/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QuantConnect.Exceptions;

namespace QuantConnect.Tests.Common.Exceptions
{
    [TestFixture]
    public class CompositeExceptionProjectionTests
    {
        [Test]
        public void CreatesFromAssemblies()
        {
            var assembly = typeof(FakeExceptionInterpreter).Assembly;
            var projector = StackExceptionInterpreter.CreateFromAssemblies(new[] {assembly});
            Assert.AreEqual(1, projector.Interpreters.Count(p => p.GetType() == typeof(FakeExceptionInterpreter)));
        }

        [Test]
        public void CallsProjectOnFirstProjectionThatCanProject()
        {
            var canProjectCalled = new List<int>();
            var projectCalled = new List<int>();
            var projections = new[]
            {
                new FakeExceptionInterpreter(e =>
                {
                    canProjectCalled.Add(0);
                    return false;
                }, e =>
                {
                    projectCalled.Add(0);
                    return e;
                }),
                new FakeExceptionInterpreter(e =>
                {
                    canProjectCalled.Add(1);
                    return true;
                }, e =>
                {
                    projectCalled.Add(1);
                    return e;
                }),
                new FakeExceptionInterpreter(e =>
                {
                    canProjectCalled.Add(2);
                    return false;
                }, e =>
                {
                    projectCalled.Add(2);
                    return e;
                })
            };

            var projector = new StackExceptionInterpreter(projections);
            projector.Interpret(new Exception(), null);

            // can project called for 1st and second entry
            Assert.Contains(0, canProjectCalled);
            Assert.Contains(1, canProjectCalled);
            Assert.That(canProjectCalled, Is.Not.Contains(2));

            // project only called on second entry
            Assert.That(projectCalled, Is.Not.Contains(0));
            Assert.Contains(1, projectCalled);
            Assert.That(projectCalled, Is.Not.Contains(2));
        }

        [Test]
        public void RecursivelyProjectsInnerExceptions()
        {
            var inner = new Exception("inner");
            var middle = new Exception("middle", inner);
            var outter = new Exception("outter", middle);
            var projector = new StackExceptionInterpreter(new[]
            {
                new FakeExceptionInterpreter()
            });

            var projected = projector.Interpret(outter, null);
            Assert.AreEqual("Projected 1: outter", projected.Message);
            Assert.AreEqual("Projected 2: middle", projected.InnerException.Message);
            Assert.AreEqual("Projected 3: inner", projected.InnerException.InnerException.Message);
        }

        [Test]
        public void RecursivelyFlattensExceptionMessages()
        {
            var inner = new Exception("inner");
            var middle = new Exception("middle", inner);
            var outter = new Exception("outter", middle);
            var message = new StackExceptionInterpreter(Enumerable.Empty<IExceptionInterpreter>()).ToString(outter);
            Assert.AreEqual("outter middle inner", message);
        }
    }
}
