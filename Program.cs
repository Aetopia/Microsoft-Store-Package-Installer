using System.Collections.Generic;
using System;

class Program
{
  static void Main(string[] args)
  {
    MicrosoftStore microsoftStore = new();
    foreach (string arg in args)
      try { microsoftStore.InstallPackages(microsoftStore.GetPackages(arg)); }
      catch { }
  }
}