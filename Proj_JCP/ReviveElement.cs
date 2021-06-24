using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel;

using Karamba.Geometry;
using Karamba.CrossSections;
using Karamba.Elements;
using Karamba.GHopper;
using Karamba.Loads;
using Karamba.Materials;
using Karamba.Models;
using Karamba.Nodes;
using Karamba.Supports;
using Karamba.Results;
using Karamba.Utilities;
using Proj_JCP.Properties;

namespace Proj_JCP
{
    public class ReviveElement : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ReviveElement class.
        /// </summary>
        public ReviveElement()
          : base("ReviveElement", "ReviveElement",
              "Description",
              "DDL", "JCP")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Karamba.GHopper.Models.Param_Model(), "inModel", "inModel", "Model to be manipulated", GH_ParamAccess.item);
            pManager.AddNumberParameter("threshold", "thres", "", GH_ParamAccess.item);
            pManager.AddIntegerParameter("max_iteration", "iter", "", GH_ParamAccess.item);

            pManager.AddTextParameter("col id", "col id", "elemid subject to elimination", GH_ParamAccess.item);
            pManager.AddTextParameter("gird id", "gird id", "girder", GH_ParamAccess.item);
            pManager.AddTextParameter("beam id", "beam id", "beam subject to elimination", GH_ParamAccess.item);

            pManager.AddIntegerParameter("lcs", "lcs", "load cases", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddParameter(new Karamba.GHopper.Models.Param_Model(), "outModel", "outModel", "output of Karamba Model after manipulation", GH_ParamAccess.item);
            // pManager.AddBooleanParameter("isActive", "isActive", "list of bool vals", GH_ParamAccess.list);
            pManager.AddIntegerParameter("loop_cnt", "loop_cnt", "", GH_ParamAccess.item);
            pManager.AddBooleanParameter("isConverged", "isConverged", "", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            Karamba.GHopper.Models.GH_Model in_gh_model = null;
            int loopcnt = 0;
            int max_iter = 0;
            double thres = 0;
            string cid = "";
            string bid = "";
            string gid = "";
            bool thresFlag = false;

            List<int> lcs = new List<int>();

            //Dictionary<int, List<int>> nodeColDic = new Dictionary<int, List<int>>();
            Dictionary<int, List<int>> nodeBeamDic = new Dictionary<int, List<int>>();
            Dictionary<int, List<int>> nodeGirDic = new Dictionary<int, List<int>>();

            List<int> exclusion_ids = new List<int>();
            List<ModelElement> targetElems = new List<ModelElement>();

            // --- input ---
            if (!DA.GetData(0, ref in_gh_model)) return;
            Model model = in_gh_model.Value;
            if (!DA.GetData(1, ref thres)) return;
            if (!DA.GetData(2, ref max_iter)) return;

            if (!DA.GetData(3, ref cid)) return;
            DA.GetData(4, ref gid);
            DA.GetData(5, ref bid);

            if (!DA.GetDataList(6, lcs)) return;

            // --- solve ---

            // clone model to avoid side effects
            model = model.Clone();

            // clone its elements to avoid side effects
            model.cloneElements();

            // clone the feb-model to avoid side effects
            model.deepCloneFEModel();

            // prepare error message
            string singular_system_msg = "The stiffness matrix of the system is singular.";



            // prepare nodeColumeDic
            // prepare nodeBeamDic
            // prepare nodeGirDic
            foreach (ModelElement e in model.elems)
            {
                if (e.id == bid) // bid = "B1"
                {
                    List<int> nids = new List<int>() { e.node_inds[0], e.node_inds[1] };

                    foreach (int n in nids)
                    {
                        if (nodeBeamDic.ContainsKey(n))
                        {
                            nodeBeamDic[n].Add(e.ind);
                        }
                        else
                        {
                            nodeBeamDic.Add(n, new List<int>() { e.ind });
                        }
                    }
                }

                else if (e.id == gid) // gid = "G1"
                {
                    List<int> nids = new List<int>() { e.node_inds[0], e.node_inds[1] };

                    foreach (int n in nids)
                    {
                        if (nodeGirDic.ContainsKey(n))
                        {
                            nodeGirDic[n].Add(e.ind);
                        }
                        else
                        {
                            nodeGirDic.Add(n, new List<int>() { e.ind });
                        }
                    }
                }

                else if (e.id.Contains(cid)) // cid = "C1"
                {
                    targetElems.Add(e);
                }

            }

            // iteration of analyses
            // for (int iter = 0; iter < max_iter; iter++)

            bool recalcFlag = true;
            double maxVal = new double();
            int max_id = -999;
            IOrderedEnumerable<KeyValuePair<int, double>> sortedDicDeltaId = new Dictionary<int, double>().OrderByDescending((x) => x.Value);

            // create a deform and response object for calculating and retriving results
            feb.Deform deform = new feb.Deform(model.febmodel);
            feb.Response response = new feb.Response(deform);

            while (loopcnt < max_iter)
            {

                if (recalcFlag == true)
                {
                    // create a deform and response object for calculating and retriving results
                    deform = new feb.Deform(model.febmodel);
                    response = new feb.Response(deform);

                    try
                    {
                        // calculate the displacements
                        response.updateNodalDisplacements();
                        // calculate the member forces
                        response.updateMemberForces();
                    }
                    catch
                    {
                        // send an error message in case something went wrong
                        throw new Exception(singular_system_msg);
                    }

                    // generate deformation list
                    var dicDeltaId = new Dictionary<int, double>();
                    for (int i = 0; i < model.elems.Count; i++)
                    {
                        ModelElement elem = model.elems[i];
                        if (elem.id.ToString() != "dummy")
                        {
                            dicDeltaId.Add(i, 0.0);
                            continue;
                        }

                        ModelTruss mt = (ModelTruss)elem;
                        double length = mt.elementLength(model);

                        // obtain area A [m2]
                        CroSec_Beam bCroSec = (CroSec_Beam)elem.crosec;
                        double area = bCroSec.A;

                        // obtain young E [kN/m2]
                        FemMaterial_Isotrop mat = (FemMaterial_Isotrop)bCroSec.material;
                        double E = mat.E(0);

                        // retrieve resultant cross section forces [kN]

                        List<double> Ns = new List<double>();
                        foreach (int lc in lcs)
                        {
                            elem.resultantCroSecForces(model, lc, out double N, out double V, out double M);
                            Ns.Add(Math.Abs(N));
                        }

                        double Nmax = Ns.Max();

                        // calculate delta
                        double delta = Nmax * length / (E * area);
                        dicDeltaId.Add(i, Math.Abs(delta));

                    }

                    sortedDicDeltaId = dicDeltaId.OrderByDescending((x) => x.Value);
                    maxVal = sortedDicDeltaId.First().Value;
                }
                
                // finish before max_iter
                if (maxVal < thres)
                {
                    thresFlag = true;
                    break;
                }

                // find the line to revive
                foreach (var v in sortedDicDeltaId) 
                {
                    int dict_id = v.Key;
                    if (exclusion_ids.Contains(dict_id))
                    {
                        continue;
                    }
                    else
                    {
                        max_id = v.Key;
                        break;
                    }
                }

                int max_dum_line_n0id = model.elems[max_id].node_inds[0];

                // detect the elemid to revive
                bool flag = false;
                foreach (ModelElement elem in targetElems)
                {
                    if (elem.is_active == true)
                    {
                        exclusion_ids.Add(max_id);
                        continue;
                    }

                    if (elem.node_inds[0] == max_dum_line_n0id)
                    {
                        elem.set_is_active(model, true);

                        if (nodeBeamDic.Count != 0)
                        {
                            ElimElem.ReviveBeamActivity(nodeGirDic, nodeBeamDic, elem, model);
                        }

                        exclusion_ids.Add(max_id);
                        flag = true;
                        break;
                    }
                }

                if (flag == false)
                {
                    exclusion_ids.Add(max_id);
                    recalcFlag = false;
                }
                else
                {
                    loopcnt += 1;
                    // if something changed inform the feb-model about it
                    // (otherwise it won't recalculate)
                    recalcFlag = true;

                    model.febmodel.touch();

                    // this guards the objects from being freed prematurely
                    GC.KeepAlive(deform);
                    GC.KeepAlive(response);
                }
            }

            // update the model to its final state
            try
            {
                // create a deform and response object for calculating and retrieving results
                deform = new feb.Deform(model.febmodel);
                response = new feb.Response(deform);

                // calculate the displacement
                response.updateNodalDisplacements();

                // calculate the member forces
                response.updateMemberForces();

                // maxDisp = response.maxDisplacement();

                // this guards the objects from being freed prematurely
                GC.KeepAlive(deform);
                GC.KeepAlive(response);
            }
            catch
            {
                // send an error message in case something went wrong
                throw new Exception(singular_system_msg);
            }


            // --- output ---

            DA.SetData(0, new Karamba.GHopper.Models.GH_Model(model));
            DA.SetData(1, loopcnt);
            DA.SetData(2, thresFlag);

        }



        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return Resources.revive;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("57eb8549-ec20-4784-b557-ffbd0b887fe4"); }
        }

    }
}