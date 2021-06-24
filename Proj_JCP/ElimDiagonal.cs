using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;


using Karamba.Models;
using Karamba.Elements;
using feb;
using Karamba.Nodes;
using System.Collections.ObjectModel;
using Karamba.Geometry;
using Grasshopper.Kernel.Geometry;
using Karamba.GHopper.Results;
using Rhino.Display;
using Proj_JCP.Properties;

// In order to load the result of this wizard, you will also need to
// add the output bin/ folder of this project to the list of loaded
// folder in Grasshopper.
// You can use the _GrasshopperDeveloperSettings Rhino command for that.

namespace Proj_JCP
{
    public class ElimDiag : GH_Component
    {
        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public ElimDiag()
          : base("ElimDiag", "ElimDiag",
              "Description",
              "DDL", "JCP")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new Karamba.GHopper.Models.Param_Model(), "inModel", "inModel", "Model to be manipulated", GH_ParamAccess.item);
            pManager.AddIntegerParameter("repNum", "repNum", "number of repetition", GH_ParamAccess.item);
            pManager.AddIntegerParameter("targetNum", "targetNum", "number of target remaining members", GH_ParamAccess.item);
            pManager.AddIntegerParameter("lc", "lc", "load case to be considered", GH_ParamAccess.item);
            pManager.AddTextParameter("col id", "col id", "elemid subject to elimination", GH_ParamAccess.item);
            pManager.AddTextParameter("gird id", "gird id", "girder", GH_ParamAccess.item);
            pManager.AddTextParameter("beam id", "beam id", "beam subject to elimination", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddParameter(new Karamba.GHopper.Models.Param_Model(), "outModel", "outModel", "output of Karamba Model after manipulation", GH_ParamAccess.item);
            pManager.AddBooleanParameter("isActive", "isActive", "list of bool vals", GH_ParamAccess.list);
            pManager.AddIntegerParameter("removedElemId", "reid", "", GH_ParamAccess.list);
            pManager.AddLineParameter("removedLns", "rLns", "", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            Karamba.GHopper.Models.GH_Model in_gh_model = null;
            int max_iter = 0;
            int target_num = 0;
            int lc = 0;
            string cid = "";
            string bid = "";
            string gid = "";
            Dictionary<int, List<int>> nodeColDic = new Dictionary<int, List<int>>();
            Dictionary<int, List<int>> nodeBeamDic = new Dictionary<int, List<int>>();
            Dictionary<int, List<int>> nodeGirDic = new Dictionary<int, List<int>>();

            List<ModelElement> targetElems = new List<ModelElement>();

            List<Line> rmLns = new List<Line>();
            List<int> rmLnIds = new List<int>();


            // --- input --- 
            if (!DA.GetData(0, ref in_gh_model)) return;
            Karamba.Models.Model model = in_gh_model.Value;
            if (!DA.GetData(1, ref max_iter)) return;
            if (!DA.GetData(2, ref target_num)) return;
            if (!DA.GetData(3, ref lc)) return;
            if (!DA.GetData(4, ref cid)) return;
            DA.GetData(5, ref gid);
            DA.GetData(6, ref bid);

            // --- solve ---
            // clone model to avoid side effects
            model = model.Clone();

            // clone its elements to avoid side effects
            model.cloneElements();

            // clone the feb-model to avoid side effects
            model.deepCloneFEModel();

            string singular_system_msg = "The stiffness matrix of the system is singular.";

            int num_elem = 0;

            // prepare nodeColumeDic
            // prepare nodeBeamDic
            // prepare nodeGirDic
            foreach (ModelElement e in model.elems)
            {
                if (e as ModelShell != null)
                {
                    continue;
                }

                if (e.id.Contains("dummy"))
                {
                    continue;
                }

                if (e.id.Contains(cid)) // cid = "C1"
                {

                    num_elem += 1;
                    targetElems.Add(e);

                    List<int> nids = new List<int>() { e.node_inds[0], e.node_inds[1] };

                    foreach (int n in nids)
                    {
                        if (nodeColDic.ContainsKey(n))
                        {
                            nodeColDic[n].Add(e.ind);
                        }
                        else
                        {
                            nodeColDic.Add(n, new List<int>() { e.ind });
                        }
                    }
                }

                else if (e.id == bid) // bid = "B1"
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
            }


            // loop analysis
            int num_removed_total = 0;
            for (int iter = 0; iter < max_iter; iter++)
            {

                // num of elements to be removed at each step
                int num_remove_at_each_step = (int)(num_elem - num_removed_total - target_num) / (max_iter - iter);

                // create a deform and response object for calculating and retriving results
                feb.Deform deform = new feb.Deform(model.febmodel);
                feb.Response response = new feb.Response(deform);

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

                // generate N-List
                List<double> Normals = new List<double>();
                double N, V, M;
                foreach (ModelElement elem in targetElems)
                {
                    if (elem.is_active == false)
                    {
                        continue;
                    }

                    elem.resultantCroSecForces(model, lc, out N, out V, out M);
                    Normals.Add(Math.Abs(N));

                }
                Normals.Sort();
                double thres_N = Normals[num_remove_at_each_step];

                // check the normal force of each element and deactivate those under tension
                // double N, V, M;
                int Num_RemovedElement_ES = 0;
                foreach (ModelElement elem in targetElems)
                {
                    if (elem.is_active == false)
                    {
                        continue;
                    }

                    // retrieve resultant cross section forces
                    elem.resultantCroSecForces(model, lc, out N, out V, out M);
                    if (Math.Abs(N) <= thres_N)
                    {
                        elem.set_is_active(model, false);
                        Num_RemovedElement_ES += 1;
                        num_removed_total += 1;

                        rmLnIds.Add(elem.ind);
                        var sp = model.nodes[elem.node_inds[0]].pos;
                        var ep = model.nodes[elem.node_inds[1]].pos;
                        rmLns.Add(new Line(sp.X, sp.Y, sp.Z, ep.X, ep.Y, ep.Z));

                        if (nodeBeamDic.Count != 0)
                        {
                            SetBeamsInactive(model, bid);
                        }

                    }

                    if (Num_RemovedElement_ES == num_remove_at_each_step)
                    {
                        break;
                    }
                }

                foreach (ModelElement elem in targetElems)
                {
                    if (elem.is_active == false)
                    {
                        continue;
                    }

                    // C1 is active
                    // check the connectivity of beams
                    // need revival of beams
                    if (nodeBeamDic.Count != 0)
                    {
                        ReviveBeamActivity(nodeGirDic, nodeBeamDic, elem, model);
                    }
                }

                // if something changed inform the feb-model about it
                // (otherwise it won't recalculate)
                model.febmodel.touch();

                // this guards the objects from being freed prematurely
                deform.Dispose();
                response.Dispose();
            }

            // update the model to its final state
            try
            {
                // create a deform and response object for calculating and retrieving results
                feb.Deform deform = new feb.Deform(model.febmodel);
                feb.Response response = new feb.Response(deform);

                // calculate the displacement
                response.updateNodalDisplacements();

                // calculate the member forces
                response.updateMemberForces();


                // this guards the objects from being freed prematurely
                deform.Dispose();
                response.Dispose();
            }
            catch
            {
                // send an error message in case something went wrong
                throw new Exception(singular_system_msg);
            }

            // set up list of true/false values that corresponds to the element states
            List<bool> elem_activity = new List<bool>();
            foreach (ModelElement elem in model.elems)
            {
                elem_activity.Add(elem.is_active);
            }



            // --- output ---
            DA.SetData(0, new Karamba.GHopper.Models.GH_Model(model));
            DA.SetDataList(1, elem_activity);

            DA.SetDataList(2, rmLnIds);
            DA.SetDataList(3, rmLns);
        }

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                // You can add image files to your project resources and access them like this:
                //return Resources.IconForThisComponent;
                return Resources.remove;
            }
        }

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("b9e5029e-d96d-4a0c-9966-39976da57c12"); }
        }

        private void SetBeamsInactive(
            Karamba.Models.Model model,
            string bid)
        {
            foreach (ModelElement e in model.elems)
            {
                if (e.id != bid) continue;
                e.set_is_active(model, false);
            }

            return;
        }

        public static void ReviveBeamActivity(
            Dictionary<int, List<int>> nGD,
            Dictionary<int, List<int>> nBD,
            ModelElement elem,
            Karamba.Models.Model model)
        {
            // dealing with connecting beams
            List<int> nids = new List<int>()
                            { elem.node_inds[0], elem.node_inds[1] };
            foreach (int n in nids) // for both nodes of the active column
            {
                if (nGD.ContainsKey(n) == true)
                {
                    continue;
                }

                if (nBD.ContainsKey(n) == false)
                {
                    continue;
                }

                var bids = nBD[n];

                foreach (int b in bids)
                {
                    if (model.elems[b].is_active == false)
                    {
                        model.elems[b].set_is_active(model, true);
                    }

                    // obtain the node id of the opposite side of beam b
                    int nxtn = GetOppositeNodeId(b, n, model);

                    if (nGD.ContainsKey(nxtn) == true)
                    {
                        continue;
                    }

                    bool loopBl = true;
                    int loopcnt = 0;
                    int targetBeam = b;
                    int currentNode = n;
                    while (loopBl)
                    {
                        loopcnt++;
                        //
                        // obtain the node id of the opposite side of beam targetBeam
                        int nxtNode = GetOppositeNodeId(targetBeam, currentNode, model);
                        // 
                        if (nGD.ContainsKey(nxtNode) == true)
                        {
                            break;
                        }

                        // new beams
                        var bms = nBD[nxtNode];

                        Point3 nxt = model.nodes[nxtNode].pos;
                        Point3 current = model.nodes[currentNode].pos;
                        Vector2d v0 =
                            new Vector2d(nxt.X - current.X, nxt.Y - current.Y);
                        v0.Unitize();

                        foreach (int bn in bms)
                        {
                            if (bn == targetBeam) continue;
                            else
                            {
                                int oppNode = GetOppositeNodeId
                                    (bn, nxtNode, model);
                                Point3 opp = model.nodes[oppNode].pos;
                                Vector2d v1 =
                                    new Vector2d(opp.X - nxt.X, opp.Y - nxt.Y);
                                v1.Unitize();

                                double cos = (v0.X * v1.X + v0.Y * v1.Y) / (v0.Length * v1.Length);

                                if (cos > 0.866) // cos 60 deg
                                {
                                    model.elems[bn].set_is_active(model, true);
                                    targetBeam = bn;
                                    currentNode = nxtNode;

                                    break;
                                }
                            }
                        }

                        if (loopcnt > 3)
                        {
                            loopBl = false;
                        }

                    }
                }
            }

            return;
        }

        private static int GetOppositeNodeId(int beamId, int currentNode, Karamba.Models.Model model)
        {

            // obtain the node id of the opposite side of beam targetBeam
            var bmnds = model.elems[beamId].node_inds;
            int nxtNode;
            if (bmnds[0] == currentNode) nxtNode = bmnds[1];
            else nxtNode = bmnds[0];
            // 



            return nxtNode;
        }

    }
}
