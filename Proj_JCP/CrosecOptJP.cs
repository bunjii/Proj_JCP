using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel;
using Rhino.Geometry;

using Karamba.Loads;
using Karamba.Models;
using Karamba.Elements;
using Karamba.Supports;
using Karamba.Geometry;
using Karamba.CrossSections;
using Karamba.Results;
using KarambaCommon.Factories;
using Proj_JCP.Properties;
using System.Management.Instrumentation;

namespace Proj_JCP
{
    public class CrosecOptJP : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the CrosecOptJP class.
        /// </summary>
        public CrosecOptJP()
          : base("CrosecOptJP", "CrosecOptJP",
              "CrosecOptJP",
              "DDL", "JCP")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Karamba.GHopper.Models.Param_Model(), "inModel", "inModel", "Model to be manipulated", GH_ParamAccess.item);
            pManager.AddParameter(new Karamba.GHopper.CrossSections.Param_CrossSection(), "CroSecs", "CroSecs", "", GH_ParamAccess.list);
            pManager.AddTextParameter("targetId", "targetId", "targetId", GH_ParamAccess.item);
            pManager.AddIntegerParameter("lcs", "lcs", "load cases", GH_ParamAccess.list);
            pManager.AddIntegerParameter("buckling case", "buckling case", "", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddParameter(new Karamba.GHopper.Models.Param_Model(), "outModel", "outModel", "output of Karamba Model after manipulation", GH_ParamAccess.item);
            pManager.AddPointParameter("pts", "pts", "", GH_ParamAccess.list);
            pManager.AddIntegerParameter("csids", "csids", "", GH_ParamAccess.list);
            pManager.AddIntegerParameter("lcids", "lcids", "", GH_ParamAccess.list);
            pManager.AddNumberParameter("max N", "max N", "", GH_ParamAccess.list);
            pManager.AddNumberParameter("phi", "phi", "", GH_ParamAccess.list);
            pManager.AddNumberParameter("utils", "utils", "", GH_ParamAccess.list);
            pManager.AddNumberParameter("P_crit", "P_crit", "", GH_ParamAccess.list);

            pManager.AddIntegerParameter("loopcnt", "loopcnt", "", GH_ParamAccess.item);

        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            Karamba.GHopper.Models.GH_Model in_gh_model = null;
            List<Karamba.GHopper.CrossSections.GH_CrossSection> gh_crosecs = new List<Karamba.GHopper.CrossSections.GH_CrossSection>();
            String targetId = "";

            List<ModelElement> targetElements = new List<ModelElement>();
            List<int> lcs = new List<int>();
            int buck_case = new int();

            List<Point3d> mpts = new List<Point3d>();
            
            List<string> csids = new List<string>();
            List<int> lcids = new List<int>();

            Dictionary<int, int> targetElemDic = new Dictionary<int, int>();

            List<double> maxNout = new List<double>();

            List<double> phis = new List<double>();
            List<double> capas = new List<double>();

            List<double> pcrits = new List<double>();


            // --- input ---
            if (!DA.GetData(0, ref in_gh_model)) return;
            Model model = in_gh_model.Value;
            if (!DA.GetDataList(1, gh_crosecs)) return;
            if (!DA.GetData(2, ref targetId)) return;
            if (!DA.GetDataList(3, lcs)) return;
            DA.GetData(4, ref buck_case);

            // --- solve ---
            // clone model to avoid side effects
            model = model.Clone();

            // clone its elements to avoid side effects
            model.cloneElements();

            // clone the feb-model to avoid side effects
            model.deepCloneFEModel();

            string singular_system_msg = "Stiffness Matrix Singular";

            // get nodes
            List<Point3d> pts = new List<Point3d>();
            foreach (Karamba.Nodes.Node n in model.nodes)
            {
                pts.Add(new Point3d(n.pos.X, n.pos.Y, n.pos.Z));
            }

            List<CroSec> cross_sections =
                gh_crosecs.Select(x => x.Value).ToList();

            // foreach (ModelElement elem in model.elems)
            for (int i=0; i<model.elems.Count;i++)
            {
                ModelElement elem = model.elems[i];

                if (elem.id.Contains(targetId) == false) continue;

                if (elem.is_active == false) continue;

                targetElemDic.Add(targetElements.Count, i);

                elem.crosec = cross_sections[0];
                targetElements.Add(elem);
            }
            
            foreach (ModelElement t in targetElements)
            {
                int sp = t.node_inds[0];
                int ep = t.node_inds[1];

                Point3d mpt = new Point3d(
                    0.5 * (pts[sp].X + pts[ep].X),
                    0.5 * (pts[sp].Y + pts[ep].Y),
                    0.5 * (pts[sp].Z + pts[ep].Z)
                    );

                mpts.Add(mpt);

            }

            // loop 
            int loopcnt = 0;
            for (int iter = 0; iter < cross_sections.Count - 1; iter++)
            {
                loopcnt += 1;

                lcids.Clear();
                csids.Clear();
                maxNout.Clear();

                phis.Clear();
                capas.Clear();

                pcrits.Clear();

                // model = k3d.Algorithms.AnalyzeThI(model, out max_disp, out out_g, out out_comp, out message);

                feb.Deform deform = new feb.Deform(model.febmodel);
                feb.Response response = new feb.Response(deform);

                try
                {
                    response.updateNodalDisplacements();
                    response.updateMemberForces();
                }
                catch
                {
                    throw new Exception(singular_system_msg);
                }


                for (int i=0; i<targetElements.Count;i++)
                {
                    ModelElement elem = targetElements[i];

                    // avoid side effect
                    elem = elem.Clone();
                    int modelelemid = targetElemDic[i];
                    // targetElements[i] = elem;

                    List<double> Ns = new List<double>();
                    foreach (int lc in lcs)
                    {
                        elem.resultantCroSecForces(model, lc, out double N, out double V, out double M);
                        Ns.Add(N);
                    }

                    double Nmax = Ns.Max();
                    double Nmin = Ns.Min();

                    CroSec_Beam bCroSec = (CroSec_Beam) elem.crosec;
                    double area = bCroSec.A * 1000000; // [mm2]

                    // calc of radius of gyration i [mm]
                    double r_g = Math.Sqrt(bCroSec.Iyy / bCroSec.A) * 1000; // [mm]
                    // calculate element length L [mm]
                    int sid = elem.node_inds[0];
                    int eid = elem.node_inds[1];
                    double length = pts[sid].DistanceTo(pts[eid]) * 1000; // [mm]
                    
                    // calc of lambda
                    double lambda = length / r_g; // [-]
                   
                    // obtain young E [kN/m2] --> [N/mm2]
                    Karamba.Materials.FemMaterial_Isotrop mat 
                        = (Karamba.Materials.FemMaterial_Isotrop)bCroSec.material;
                    double E = mat.E(0) * 0.001; // [N/mm2]

                    // critical loading Euler in [N]
                    double P_crit = Math.Pow(Math.PI, 2) * E 
                        * (bCroSec.Iyy * 1000000000000) / Math.Pow(length,2); 
                    
                    // obtain yield strength F [N/mm2]
                    double Fy = mat.fy(0) / 1000;
                    
                    // overwrite F with Chinese Code
                    CroSec_Circle cCroSec = (CroSec_Circle)bCroSec;
                    double t = cCroSec.thick * 1000; // [mm]

                    double ft = new double();

                    if (t <= 16)
                    {
                        Fy = 345;
                        ft = 305;
                    }
                    else if (t <= 40)
                    {
                        Fy = 335;
                        ft = 295;
                    }
                    else if (t <= 63)
                    {
                        Fy = 325;
                        ft = 290;
                    }
                    else
                    {
                        Fy = 305;
                        ft = 270;
                    }

                    //_ CN code
                    double lambda_n = lambda / Math.PI * Math.Sqrt(Fy / E);
                    double phi = new double();
                    if (lambda_n <= 0.215)
                    {
                        phi = 1 - 0.41 * Math.Pow(lambda_n, 2);
                    }
                    else
                    {
                        phi = 1 / (2 * Math.Pow(lambda_n, 2))
                            *(
                                (0.986+0.152*lambda_n+Math.Pow(lambda_n,2)) 
                                - Math.Sqrt(
                                    (Math.Pow(0.986 + 0.152 * lambda_n + Math.Pow(lambda_n, 2), 2))
                                    -4*Math.Pow(lambda_n, 2)
                                    ) 
                              );
                    }

                    double ax_capacity = phi * area * ft; // [N]

                    List<double> utils = new List<double>();
                    for (int j = 0; j < Ns.Count(); j++)
                    {
                        // tension case or compression case
                        if (Ns[j] < 0)
                        {
                            // compression
                            if (lcs[j] == buck_case)
                            {
                                if (ax_capacity > (P_crit / 2.0))
                                {
                                    ax_capacity = P_crit / 2.0;
                                }

                                utils.Add(Math.Abs(Ns[j]) * 1000 / ax_capacity);

                            }
                            else
                            {
                                utils.Add(Math.Abs(Ns[j]) * 1000 / ax_capacity);

                            }

                        }
                        else
                        {
                            // tension
                            ax_capacity = ft * area;

                            utils.Add(Ns[j] * 1000 / ax_capacity);

                        }

                    }

                    double util_max = utils.Max();
                    int max_id = utils.IndexOf(util_max);

                    lcids.Add(max_id);
                    maxNout.Add(Ns[max_id]);

                    if (util_max > 1.0)
                    {
                        // find current cross-section index
                        int id_current = -999;
                        for (int j = 0; j < cross_sections.Count(); j++)
                        {
                            if (elem.crosec.name == cross_sections[j].name)
                            {
                                id_current = j;
                                break;
                            }
                        }
                        if (id_current < cross_sections.Count() - 1)
                        {
                            elem.crosec = cross_sections[id_current + 1];
                        }
                    }

                    /*
                    else
                    {
                        // find current cross-section index
                        int id_current = -999;
                        for (int j = 0; j < cross_sections.Count(); j++)
                        {
                            if (elem.crosec.name == cross_sections[j].name)
                            {
                                id_current = j;
                                break;
                            }
                        }
                        if (id_current != 0)
                        {
                            elem.crosec = cross_sections[id_current - 1];


                            bCroSec = (CroSec_Beam)elem.crosec;
                            area = bCroSec.A * 1000000; // [mm2]

                            // calc of radius of gyration i [mm]
                            r_g = Math.Sqrt(bCroSec.Iyy / bCroSec.A) * 1000; // [mm]


                            // calc of lambda
                            lambda = length / r_g; // [-]

                            // critical loading Euler in [N]
                            P_crit = Math.Pow(Math.PI, 2) * E
                                * (bCroSec.Iyy * 1000000000000) / Math.Pow(length, 2);

                            // overwrite F with Chinese Code
                            cCroSec = (CroSec_Circle)bCroSec;
                            t = cCroSec.thick * 1000; // [mm]

                            ft = new double();

                            if (t <= 16)
                            {
                                Fy = 345;
                                ft = 305;
                            }
                            else if (t <= 40)
                            {
                                Fy = 335;
                                ft = 295;
                            }
                            else if (t <= 63)
                            {
                                Fy = 325;
                                ft = 290;
                            }
                            else
                            {
                                Fy = 305;
                                ft = 270;
                            }

                            //_ CN code
                            lambda_n = lambda / Math.PI * Math.Sqrt(Fy / E);
                            phi = new double();
                            if (lambda_n <= 0.215)
                            {
                                phi = 1 - 0.41 * Math.Pow(lambda_n, 2);
                            }
                            else
                            {
                                phi = 1 / (2 * Math.Pow(lambda_n, 2))
                                    * (
                                        (0.986 + 0.152 * lambda_n + Math.Pow(lambda_n, 2))
                                        - Math.Sqrt(
                                            (Math.Pow(0.986 + 0.152 * lambda_n + Math.Pow(lambda_n, 2), 2))
                                            - 4 * Math.Pow(lambda_n, 2)
                                            )
                                      );
                            }

                            ax_capacity = phi * area * ft; // [N]

                            utils = new List<double>();
                            for (int j = 0; j < Ns.Count(); j++)
                            {
                                // tension case or compression case
                                if (Ns[j] < 0)
                                {
                                    // compression
                                    if (lcs[j] == buck_case)
                                    {
                                        if (ax_capacity > P_crit / 2.0)
                                        {
                                            ax_capacity = P_crit / 2.0;
                                        }

                                        utils.Add(Math.Abs(Ns[j]) * 1000 / ax_capacity);

                                    }
                                    else
                                    {
                                        utils.Add(Math.Abs(Ns[j]) * 1000 / ax_capacity);

                                    }

                                }
                                else
                                {
                                    // tension
                                    ax_capacity = ft * area;
                                    utils.Add(Ns[j] * 1000 / ax_capacity);

                                }



                            }

                            util_max = utils.Max();

                            if (util_max > 1.0)
                            {
                                elem.crosec = cross_sections[id_current];
                            }

                            else
                            {
                                elem.crosec = cross_sections[id_current - 1];
                            }
                        }
                    }

                    */

                    csids.Add(elem.crosec.name);

                    phis.Add(phi);
                    // capas.Add(ax_capacity / 1000);
                    capas.Add(util_max);
                    pcrits.Add(P_crit / 1000);

                    model.elems[modelelemid] = elem;
                    targetElements[i] = elem;

                }

                model.initMaterialCroSecLists();
                model.febmodel = model.buildFEModel();

                model.febmodel.touch();

                GC.KeepAlive(deform);
                GC.KeepAlive(response);

            }

            // model = k3d.Algorithms.AnalyzeThI(model, out max_disp, out out_g, out out_comp, out message);

            try
            {
                feb.Deform deform = new feb.Deform(model.febmodel);
                feb.Response response = new feb.Response(deform);

                response.updateNodalDisplacements();
                response.updateMemberForces();

                GC.KeepAlive(deform);
                GC.KeepAlive(response);
            }
            catch
            {
                throw new Exception(singular_system_msg);
            }

            foreach (ModelElement e in model.elems)
            {
                if (e.id.Contains(targetId) == false) continue;

                if (e.is_active == false) continue;

                csids.Add(e.crosec.name);
            }

            // --- output ---
            DA.SetData(0, new Karamba.GHopper.Models.GH_Model(model));
            DA.SetDataList(1, mpts);
            DA.SetDataList(2, csids);
            DA.SetDataList(3, lcids);
            DA.SetDataList(4, maxNout);

            DA.SetDataList(5, phis);
            DA.SetDataList(6, capas);
            DA.SetDataList(7, pcrits);
            DA.SetData(8, loopcnt);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Resources.crosec;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("4b71da30-86ba-4bcb-b9d4-1afc7755285a"); }
        }
    }
}