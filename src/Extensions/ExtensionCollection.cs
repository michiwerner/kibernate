/*
   Copyright 2023 Michael Werner

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

using System.Collections.Generic;
using System.Threading.Tasks;
using Kibernate.Controllers;

namespace Kibernate.Extensions;

public class ExtensionCollection : List<IExtension>, IExtension
{
    public string? Type => null;

    public async Task InvokeAsync(ControllerContext context, ExtensionDelegate next)
    {
        if (Count == 0)
        {
            await next(context);
            return;
        }

        await _RunMiddleware(context, next);
    }

    private async Task _RunMiddleware(ControllerContext context, ExtensionDelegate finalNext, int currentIndex = 0)
    {
        if (currentIndex >= Count)
        {
            await finalNext(context);
        }
        else
        {
            await this[currentIndex].InvokeAsync(context, async (ctx) => await _RunMiddleware(ctx, finalNext, currentIndex + 1));
        }
    }
}