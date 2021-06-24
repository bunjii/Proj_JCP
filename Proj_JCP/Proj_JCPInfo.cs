using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace Proj_JCP
{
    public class Proj_JCPInfo : GH_AssemblyInfo
    {
        public override string Name
        {
            get
            {
                return "ProjJCP";
            }
        }
        public override Bitmap Icon
        {
            get
            {
                //Return a 24x24 pixel bitmap to represent this GHA library.
                return null;
            }
        }
        public override string Description
        {
            get
            {
                //Return a short string describing the purpose of this GHA library.
                return "";
            }
        }
        public override Guid Id
        {
            get
            {
                return new Guid("923363a4-fa56-4cb1-ba25-dcc972c21f61");
            }
        }

        public override string AuthorName
        {
            get
            {
                //Return a string identifying you or your company.
                return "";
            }
        }
        public override string AuthorContact
        {
            get
            {
                //Return a string representing your preferred contact details.
                return "";
            }
        }
    }
}
